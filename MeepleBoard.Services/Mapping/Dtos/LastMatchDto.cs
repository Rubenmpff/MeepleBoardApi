namespace MeepleBoard.Services.Mapping.Dtos
{
    public class LastMatchDto
    {
        public string Name { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Winner { get; set; } = "Desconhecido";
    }
}