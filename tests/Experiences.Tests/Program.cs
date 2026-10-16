// Ratings closed per experience and experience publications: domain rules. No database.
using MeepleBoard.Domain.Entities;

var passed = 0;
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
void Throws<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { return; } throw new Exception(message); }
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }

Check("a rating is given once: zero is a rating; a different value is refused; notes and tags still change", () => {
    var e = new MatchJournalEntry(Guid.NewGuid(), Guid.NewGuid());
    e.Update(null, "draft note", null); Assert(e.PersonalRating == null, "no rating yet is allowed");
    e.Update(0, "note", "tag"); Assert(e.PersonalRating == 0, "zero is a rating");
    Throws<InvalidOperationException>(() => e.Update(7.5, "note", "tag"), "changing a saved rating must be refused");
    e.Update(0, "new note", "new tag"); Assert(e.PersonalRating == 0 && e.Notes == "new note" && e.Tags == "new tag", "same rating with new texts");
    e.Update(null, "only texts", null); Assert(e.PersonalRating == 0 && e.Notes == "only texts", "sending no rating keeps the saved one");
    Throws<ArgumentException>(() => new MatchJournalEntry(Guid.NewGuid(), Guid.NewGuid()).Update(7.25, null, null), "half points only");
});

Check("publishing the rating and the comment are independent and explicit", () => {
    var p = new MatchExperiencePublication(Guid.NewGuid());
    Assert(p.IsEmpty && !p.IsVisible, "nothing published by default");
    p.Set(showRating: false, comment: "Draft text", publishComment: false);
    Assert(p.Comment == "Draft text" && !p.IsVisible, "a kept comment is private until published");
    p.Set(true, "Draft text", false); Assert(p.RatingPublishedAt != null && p.CommentPublishedAt == null && p.IsVisible, "rating only");
    p.Set(false, "Draft text", true); Assert(p.RatingPublishedAt == null && p.CommentPublishedAt != null, "comment only");
    Throws<ArgumentException>(() => p.Set(true, "  ", true), "an empty comment cannot be published");
    Throws<ArgumentException>(() => p.Set(true, new string('x', 501), true), "500 characters at most");
});

Check("editing a published comment keeps it published and marks it edited; withdrawing or deleting keeps nothing else changed", () => {
    var p = new MatchExperiencePublication(Guid.NewGuid());
    p.Set(true, "First", true); var published = p.CommentPublishedAt;
    p.Set(true, "Second", true); Assert(p.CommentPublishedAt == published && p.CommentEditedAt != null && p.Comment == "Second", "edited, still published");
    p.WithdrawRating(); Assert(p.RatingPublishedAt == null && p.CommentPublishedAt != null, "rating withdrawn alone");
    p.WithdrawComment(); Assert(p.CommentPublishedAt == null && p.Comment == "Second" && !p.IsVisible, "comment back to private, text kept");
    p.DeleteComment(); Assert(p.Comment == null && p.IsEmpty, "deleted");
});

Check("moderation hides with a known reason and restores; reports need a known reason and short detail", () => {
    var p = new MatchExperiencePublication(Guid.NewGuid()); p.Set(true, "Text", true);
    p.Hide("PersonalData", Guid.NewGuid()); Assert(!p.IsVisible && p.HiddenReason == "PersonalData", "hidden");
    Throws<ArgumentException>(() => p.Hide("Whatever", null), "unknown reason");
    p.Restore(); Assert(p.IsVisible, "restored");
    p.Hide(ExperienceReport.AutoHidden, null); Assert(!p.IsVisible, "automatic hiding after reports");
    Throws<ArgumentException>(() => new ExperienceReport(p.Id, Guid.NewGuid(), "Nope", null), "unknown report reason");
    Throws<ArgumentException>(() => new ExperienceReport(p.Id, Guid.NewGuid(), "Other", new string('x', 301)), "detail up to 300");
    var r = new ExperienceReport(p.Id, Guid.NewGuid(), "Spam", " detail "); Assert(r.IsOpen && r.Detail == "detail", "open report");
    r.Resolve(Guid.NewGuid(), "keep"); Assert(!r.IsOpen, "resolved");
});

Console.WriteLine($"{passed} checks passed");
