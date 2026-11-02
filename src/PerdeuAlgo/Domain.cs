using System.Globalization;
using System.Text;

namespace PerdeuAlgo;

public sealed class RuleException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
public record RegisterInput(string Name, string Email, string Password);
public record LoginInput(string Email, string Password);
public record UserView(long Id, string Name);
public record ItemInput(string Kind, string Title, string Category, string Location, DateOnly Date, string Description, string Secret);
public record Item(long Id, long OwnerId, string Kind, string Title, string Category, string Location, DateOnly Date, string Description, string Status);
public record ClaimInput(string Proof);
public record Claim(long Id, long ItemId, long ClaimantId, string ClaimantName, string Title, string Proof, string Status, string CreatedAt, string OwnerName);
public record Match(Item Item, int Score, string[] Reasons);

public static class Rules
{
    public static readonly string[] Categories = ["Livros", "Eletrônicos", "Documentos", "Acessórios", "Roupas", "Outros"];
    public static string Text(string? value, string field, int min, int max)
    {
        var text = value?.Trim() ?? "";
        if (text.Length < min || text.Length > max) throw new RuleException($"{field}: use entre {min} e {max} caracteres.");
        return text;
    }
    public static void Validate(ItemInput input, DateOnly today)
    {
        if (input.Kind is not ("lost" or "found")) throw new RuleException("Tipo de registro inválido.");
        if (!Categories.Contains(input.Category)) throw new RuleException("Categoria inválida.");
        Text(input.Title, "Título", 3, 90); Text(input.Location, "Local", 3, 100);
        Text(input.Description, "Descrição", 10, 1200);
        if (input.Date > today || input.Date < today.AddYears(-2)) throw new RuleException("Escolha uma data nos últimos dois anos, até hoje.");
        if (input.Kind == "found") Text(input.Secret, "Característica oculta", 5, 300);
    }
    public static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .Select(char.ToLowerInvariant).ToArray());
    public static Match? Compare(Item source, Item candidate)
    {
        if (source.Kind == candidate.Kind || source.Category != candidate.Category || source.Status != "open" || candidate.Status != "open" || source.OwnerId == candidate.OwnerId) return null;
        var lost = source.Kind == "lost" ? source : candidate;
        var found = source.Kind == "found" ? source : candidate;
        var days = found.Date.DayNumber - lost.Date.DayNumber;
        if (days < 0 || days > 14) return null;
        var score = 35;
        var reasons = new List<string> { "Mesma categoria", "Datas compatíveis" };
        score += days <= 3 ? 25 : 15;
        if (Normalize(source.Location) == Normalize(candidate.Location)) { score += 25; reasons.Add("Mesmo local"); }
        var words = Normalize(source.Title + " " + source.Description).Split([' ', ',', '.', ';', ':', '-', '/', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).ToHashSet();
        if (Normalize(candidate.Title + " " + candidate.Description).Split([' ', ',', '.', ';', ':', '-', '/', '\n'], StringSplitOptions.RemoveEmptyEntries).Any(words.Contains)) { score += 15; reasons.Add("Características em comum"); }
        return new(candidate, score, reasons.ToArray());
    }
}
