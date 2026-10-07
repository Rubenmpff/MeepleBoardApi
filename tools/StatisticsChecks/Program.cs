using MeepleBoard.Services.Statistics;
using MeepleBoard.Domain.Entities;

void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
void Reject(Action action, string label) { try { action(); throw new Exception("Accepted: " + label); } catch(ArgumentException) { Console.WriteLine("PASS " + label); } }
var user=Guid.NewGuid();var peer=Guid.NewGuid();var third=Guid.NewGuid();
var shared=MatchOutcomeRules.Resolve("COMPETITIVE","Win",[user,peer,third],[user,peer],true);
var draw=MatchOutcomeRules.Resolve("COMPETITIVE","Draw",[user,peer,third],[user,peer],false);
Check(shared[user]=="Win"&&shared[peer]=="Win"&&shared[third]=="Loss","shared win is one win per winner");
Check(draw[user]=="Draw"&&draw[third]=="Loss","partial draw gives remaining players losses");
var gameA=Guid.NewGuid();var gameB=Guid.NewGuid();var day=new DateTime(2024,10,27,12,0,0,DateTimeKind.Utc);
StatisticsRow Row(Guid game,string? mode,string? result,string? outcome,int? duration,int? score,double? rating) => new(Guid.NewGuid(),game,"Identical name",null,day,mode,result,outcome,duration,score,rating);
var rows=new List<StatisticsRow>{
 Row(gameA,"COMPETITIVE","Win",shared[user],30,17,0),
 Row(gameA,"COMPETITIVE","Draw",draw[user],null,0,7.5),
 Row(gameA,"COMPETITIVE","Draw",draw[third],0,-5,null),
 Row(gameB,"SOLO","Loss","Loss",15,-5,7.5),
 Row(gameB,"SOLO","Draw","Draw",null,0,0),
 Row(gameB,"COOPERATIVE","Win","Win",45,0,10),
 Row(gameB,"COOPERATIVE","Undefined","Undefined",null,null,null),
 Row(gameB,null,null,"Win",null,100,null), // legacy evidence must not infer a known win
};
var query=new StatisticsQuery{Start=new(2024,10,1),EndExclusive=new(2024,11,1),TimeZone="Europe/Lisbon"};
var summary=StatisticsCalculator.Summary(rows,query,StatisticsCalculator.Validate(query).Zone);
Check(summary.Matches==8&&summary.DistinctGames==2&&summary.Games.Count==2,"canonical GameId grouping even with identical names");
Check(summary.Results.Wins==2&&summary.Results.Losses==2&&summary.Results.Draws==2&&summary.Results.Known==6&&summary.Results.WithoutResult==2&&summary.Results.Legacy==1&&summary.Results.WinRate==33.33,"known denominator excludes undefined and legacy");
Check(summary.RecordedMinutes==90&&summary.MatchesWithDuration==4&&summary.MatchesWithoutDuration==4,"partial duration and explicit zero preserved");
Check(summary.AveragePersonalRating==5&&summary.RatedMatches==5,"zero and half-point ratings preserved; absence excluded");
Check(summary.Evolution.Sum(x=>x.Matches)==8&&summary.Evolution.Count==31,"zero-activity buckets and full filtered series");
foreach(var metric in new[]{"all","games","duration","missing-duration","ratings","known","wins","losses","draws","undefined","legacy"}) {
 var n=rows.Count(r=>StatisticsCalculator.MatchesMetric(r,metric));
 var expected=metric switch {"duration" or "missing-duration"=>4,"ratings"=>5,"known"=>6,"wins" or "losses" or "draws" or "undefined"=>2,"legacy"=>1,_=>8};
 Check(n==expected,"supporting rows for "+metric);
}
var solo=StatisticsCalculator.Summary(rows,new(){Start=query.Start,EndExclusive=query.EndExclusive,TimeZone=query.TimeZone,Mode="SOLO",GameId=gameB},TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone));
Check(solo.Matches==2&&solo.Results.Losses==1&&solo.Results.Draws==1,"mode and game filters share the same universe");
var missing=StatisticsCalculator.Summary([rows[6]],query,TimeZoneInfo.Utc);
Check(missing.RecordedMinutes==null&&missing.Results.WinRate==null&&missing.AveragePersonalRating==null,"no measurements are unavailable, not zero");
var zero=StatisticsCalculator.Summary([rows[2]],query,TimeZoneInfo.Utc);Check(zero.RecordedMinutes==0,"explicit zero duration is distinct from absence");
var empty=StatisticsCalculator.Summary([],query,TimeZoneInfo.Utc);Check(empty.Matches==0&&empty.DistinctGames==0&&empty.Results.WinRate==null,"empty interval");
var spring=StatisticsCalculator.Validate(new(){Start=new(2024,3,31),EndExclusive=new(2024,4,1),TimeZone="Europe/Lisbon"});
var autumn=StatisticsCalculator.Validate(new(){Start=new(2024,10,27),EndExclusive=new(2024,10,28),TimeZone="Europe/Lisbon"});
Check((spring.EndUtc-spring.StartUtc).TotalHours==23&&(autumn.EndUtc-autumn.StartUtc).TotalHours==25,"DST days use local boundaries, not 24h arithmetic");
var nearMidnight=rows[0] with{MatchDate=new(2024,6,30,23,30,0,DateTimeKind.Utc)};
Check(StatisticsCalculator.LocalDate(nearMidnight,TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon"))==new DateOnly(2024,7,1),"local date buckets cross UTC midnight");
Reject(()=>StatisticsCalculator.Validate(new(){Start=query.Start,EndExclusive=query.Start}),"reversed/empty interval rejected");
Reject(()=>StatisticsCalculator.Validate(new(){Start=query.Start,EndExclusive=query.EndExclusive,TimeZone="Invalid/Zone"}),"invalid time zone rejected");
Reject(()=>StatisticsCalculator.Validate(new(){Start=query.Start,EndExclusive=query.EndExclusive,Mode="CATALOGUE_GUESS"}),"invalid mode rejected");
Reject(()=>StatisticsCalculator.MatchesMetric(rows[0],"invalid"),"invalid metric rejected");
Console.WriteLine("Statistics deterministic checks complete; no database access.");
PairRow Pair(string? mode, string? result, string? mine, string? friend, bool otherWin=false, bool otherDraw=false) => new(Row(gameA, mode,result,mine,null,0,null), -5,friend,otherWin,otherDraw);
var pairs=new[]{ Pair("COMPETITIVE","Win","Win","Win"), Pair("COMPETITIVE","Win","Win","Loss"), Pair("COMPETITIVE","Win","Loss","Win"),
 Pair("COMPETITIVE","Win","Loss","Loss",true), Pair("COMPETITIVE","Draw","Draw","Draw"), Pair("COMPETITIVE","Draw","Draw","Loss"), Pair("COMPETITIVE","Draw","Loss","Loss",false,true),
 Pair("COOPERATIVE","Win","Win","Win"), Pair("COOPERATIVE","Loss","Loss","Loss"), Pair("COOPERATIVE","Draw","Draw","Draw"), Pair(null,null,"Win","Loss"), Pair("COOPERATIVE","Win","Win","Loss") };
var categories=new[]{"shared-win","my-only-win","friend-only-win","other-win","draw-together","draw-with-other","other-draw","team-win","team-loss","team-draw","unknown-pair","unknown-pair"};
Check(pairs.Select(CompanyCalculator.Category).SequenceEqual(categories),"pair results distinguish shared wins, partial ties, other participants, teams and legacy/inconsistent results");
var pairStats=CompanyCalculator.Results(pairs);
Check(pairStats.Known==10&&pairStats.Unknown==2&&pairStats.MyWins==2&&pairStats.FriendWins==2&&pairStats.SharedWins==1&&pairStats.Draws==2,"overlapping wins have explicit shared category; known pair sample has no double counts");
Check(pairs.All(p=>CompanyCalculator.Matches(p,"paired-scores")),"paired scores retain zero and negative values without electing a winner");
Reject(()=>CompanyCalculator.Matches(pairs[0],"invented"),"invalid pair metric rejected");

Check(CompanyCalculator.Results([Pair("COMPETITIVE","Win","Win","Loss",true)]).OtherSharedWins==1,"another participant can share a win with one of us without being counted as the sole winner");
