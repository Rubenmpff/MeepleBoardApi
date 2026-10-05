using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Token pesquisável associado a um registo do GameSearchCatalog.
    ///
    /// Esta entidade existe exclusivamente para acelerar pesquisas por
    /// início de palavra sem recorrer a pesquisas do tipo "%texto%".
    ///
    /// Exemplo:
    ///
    /// "Roll for the Galaxy"
    ///
    /// pode gerar tokens como:
    /// - for
    /// - the
    /// - galaxy
    ///
    /// A primeira palavra do título não precisa de ser persistida aqui,
    /// porque já é pesquisada através de GameSearchCatalog.NormalizedName.
    ///
    /// Tal como GameSearchCatalog, esta entidade é reconstruível a partir
    /// do catálogo BGG e não representa dados de domínio do utilizador.
    /// </summary>
    public class GameSearchToken
    {
        // Necessário para o Entity Framework.
        private GameSearchToken()
        {
            Token = string.Empty;
        }

        public GameSearchToken(
            int bggId,
            string token,
            short position)
        {
            if (bggId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bggId),
                    "O BGG ID tem de ser superior a zero.");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException(
                    "O token de pesquisa é obrigatório.",
                    nameof(token));
            }

            var normalizedToken = token.Trim();

            if (normalizedToken.Length > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(token),
                    "O token de pesquisa não pode exceder 100 caracteres.");
            }

            if (position < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "A posição do token tem de ser igual ou superior a 1.");
            }

            BggId = bggId;
            Token = normalizedToken;
            Position = position;
        }

        /// <summary>
        /// BGG ID do jogo existente em GameSearchCatalog.
        ///
        /// Em conjunto com Position forma a chave composta desta entidade.
        /// </summary>
        public int BggId { get; private set; }

        /// <summary>
        /// Palavra normalizada utilizada para pesquisa por prefixo.
        ///
        /// 100 caracteres são mais do que suficientes para um token individual
        /// e mantêm a coluna e o respetivo índice compactos.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Token { get; private set; }

        /// <summary>
        /// Posição lógica da palavra no título, ignorando a primeira palavra.
        ///
        /// Exemplo:
        /// "Roll for the Galaxy"
        ///
        /// for    -> 1
        /// the    -> 2
        /// galaxy -> 3
        ///
        /// Em conjunto com BggId forma a chave composta.
        /// </summary>
        public short Position { get; private set; }
    }
}