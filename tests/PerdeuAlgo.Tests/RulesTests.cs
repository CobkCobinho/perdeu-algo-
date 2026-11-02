using PerdeuAlgo;

namespace PerdeuAlgo.Tests;

public class RulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static ItemInput Valid => new("found", "Livro verde", "Livros", "Biblioteca", Today, "Livro de capa verde encontrado na mesa.", "Dedicatória para Ana");
    private static Item Lost => new(1, 1, "lost", "Livro verde", "Livros", "Biblioteca", Today.AddDays(-1), "Livro de capa verde", "open");
    private static Item Found => new(2, 2, "found", "Livro verde", "Livros", "Biblioteca", Today, "Livro de capa verde", "open");
    [Fact] public void Allows_valid_item() => Rules.Validate(Valid, Today);
    [Theory] [InlineData("wrong")] [InlineData("")]
    public void Rejects_invalid_kind(string kind) => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Kind = kind }, Today));
    [Fact] public void Rejects_unknown_category() => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Category = "Armas" }, Today));
    [Fact] public void Requires_secret_for_found() => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Secret = "" }, Today));
    [Fact] public void Lost_does_not_require_secret() => Rules.Validate(Valid with { Kind = "lost", Secret = "" }, Today);
    [Fact] public void Rejects_future_date() => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Date = Today.AddDays(1) }, Today));
    [Fact] public void Rejects_old_date() => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Date = Today.AddYears(-2).AddDays(-1) }, Today));
    [Theory] [InlineData(" ")] [InlineData("ab")]
    public void Rejects_short_title(string title) => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Title = title }, Today));
    [Fact] public void Rejects_long_description() => Assert.Throws<RuleException>(() => Rules.Validate(Valid with { Description = new string('x', 1201) }, Today));
    [Fact] public void Perfect_match_has_100_points() => Assert.Equal(100, Rules.Compare(Lost, Found)!.Score);
    [Fact] public void Match_ignores_case_and_accents() => Assert.Equal(100, Rules.Compare(Lost with { Location = "PÁTIO" }, Found with { Location = "patio" })!.Score);
    [Fact] public void Same_kind_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { Kind = "lost" }));
    [Fact] public void Same_owner_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { OwnerId = 1 }));
    [Fact] public void Different_category_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { Category = "Outros" }));
    [Fact] public void Finding_before_loss_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { Date = Today.AddDays(-2) }));
    [Fact] public void Finding_after_14_days_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { Date = Today.AddDays(14) }));
    [Fact] public void Closed_record_does_not_match() => Assert.Null(Rules.Compare(Lost, Found with { Status = "reserved" }));
    [Fact] public void Less_similar_match_gets_lower_score() => Assert.True(Rules.Compare(Lost, Found with { Title = "Uma obra antiga", Description = "Edição rara", Location = "Café", Date = Today.AddDays(7) })!.Score < 100);
}
