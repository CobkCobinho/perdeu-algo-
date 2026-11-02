using PerdeuAlgo;

namespace PerdeuAlgo.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly Database db = new("Data Source=:memory:");
    private readonly Service service;
    private readonly long owner, claimant, other;
    public ServiceTests()
    {
        service = new(db, TimeProvider.System);
        owner = service.Register(new("Pessoa que encontrou", "owner@example.org", "SenhaDeTeste123!")).Id;
        claimant = service.Register(new("Pessoa que perdeu", "claim@example.org", "SenhaDeTeste123!")).Id;
        other = service.Register(new("Outra pessoa", "other@example.org", "SenhaDeTeste123!")).Id;
    }
    private Item Found() => service.Create(owner, new("found", "Livro verde", "Livros", "Biblioteca", DateOnly.FromDateTime(DateTime.Now), "Livro verde encontrado na biblioteca.", "Dedicatória para Ana"));
    private long Request(long id, long? user = null) => service.Request(user ?? claimant, id, new("Tem dedicatória para Ana."));
    [Fact] public void Password_login_works() => Assert.Equal(owner, service.Login(new("OWNER@example.org", "SenhaDeTeste123!")).Id);
    [Fact] public void Wrong_password_is_rejected() => Assert.Equal(401, Assert.Throws<RuleException>(() => service.Login(new("owner@example.org", "Errada"))).Status);
    [Fact] public void Duplicate_email_is_rejected() => Assert.Equal(409, Assert.Throws<RuleException>(() => service.Register(new("Outro", "OWNER@example.org", "SenhaDeTeste123!"))).Status);
    [Theory] [InlineData("abc")] [InlineData("a@")]
    public void Invalid_email_is_rejected(string email) => Assert.Throws<RuleException>(() => service.Register(new("Pessoa", email, "SenhaDeTeste123!")));
    [Fact] public void Weak_password_length_is_rejected() => Assert.Throws<RuleException>(() => service.Register(new("Pessoa", "new@example.org", "123")));
    [Fact] public void Owner_cannot_claim_own_object() => Assert.Throws<RuleException>(() => Request(Found().Id, owner));
    [Fact] public void Cannot_claim_a_lost_record()
    {
        var lost = service.Create(owner, new("lost", "Livro verde", "Livros", "Biblioteca", DateOnly.FromDateTime(DateTime.Now), "Livro verde perdido na biblioteca.", ""));
        Assert.Throws<RuleException>(() => Request(lost.Id));
    }
    [Fact] public void Duplicate_active_claim_is_rejected() { var id = Found().Id; Request(id); Assert.Equal(409, Assert.Throws<RuleException>(() => Request(id)).Status); }
    [Fact] public void Proof_is_required() => Assert.Throws<RuleException>(() => service.Request(claimant, Found().Id, new("")));
    [Fact] public void Cannot_read_someone_elses_secret() => Assert.Equal(403, Assert.Throws<RuleException>(() => service.Secret(claimant, Found().Id)).Status);
    [Fact] public void Owner_can_read_secret() => Assert.Equal("Dedicatória para Ana", service.Secret(owner, Found().Id));
    [Fact] public void Only_owner_can_approve() { var claim = Request(Found().Id); Assert.Equal(403, Assert.Throws<RuleException>(() => service.Decide(claimant, claim, "approve")).Status); }
    [Fact] public void Approval_reserves_and_rejects_competitors()
    {
        var item = Found(); var a = Request(item.Id); var b = Request(item.Id, other);
        service.Decide(owner, a, "approve");
        Assert.Equal("reserved", service.Items().Single().Status);
        Assert.Equal("rejected", service.Claims(other).Single(c => c.Id == b).Status);
        Assert.Throws<RuleException>(() => Request(item.Id, other));
    }
    [Fact] public void Cannot_deliver_before_approval() => Assert.Equal(409, Assert.Throws<RuleException>(() => service.Decide(owner, Request(Found().Id), "deliver")).Status);
    [Fact] public void Delivery_is_terminal()
    {
        var id = Found().Id; var claim = Request(id);
        service.Decide(owner, claim, "approve"); service.Decide(owner, claim, "deliver");
        Assert.Equal("returned", service.Items().Single().Status);
        Assert.Throws<RuleException>(() => service.Decide(owner, claim, "reject"));
        Assert.Throws<RuleException>(() => service.Decide(owner, claim, "deliver"));
        Assert.Throws<RuleException>(() => service.Cancel(owner, id));
    }
    [Fact] public void Rejecting_approved_reopens_item()
    {
        var id = Found().Id; var claim = Request(id); service.Decide(owner, claim, "approve"); service.Decide(owner, claim, "reject");
        Assert.Equal("open", service.Items().Single().Status); Request(id);
    }
    [Fact] public void Cancel_rejects_pending_claims()
    {
        var id = Found().Id; Request(id); service.Cancel(owner, id);
        Assert.Equal("rejected", service.Claims(claimant).Single().Status);
        Assert.Throws<RuleException>(() => Request(id));
    }
    [Fact] public void Cannot_cancel_reserved_item()
    {
        var id = Found().Id; service.Decide(owner, Request(id), "approve"); Assert.Throws<RuleException>(() => service.Cancel(owner, id));
    }
    [Fact] public void Third_party_cannot_see_claims() { Request(Found().Id); Assert.Empty(service.Claims(other)); }
    [Fact] public void Third_party_cannot_cancel() => Assert.Equal(403, Assert.Throws<RuleException>(() => service.Cancel(other, Found().Id)).Status);
    [Fact] public void Missing_item_returns_404() => Assert.Equal(404, Assert.Throws<RuleException>(() => service.Secret(owner, 999)).Status);
    [Fact] public async Task Concurrent_approvals_have_exactly_one_winner()
    {
        var id = Found().Id; var a = Request(id); var b = Request(id, other);
        var results = await Task.WhenAll(new[] { a, b }.Select(c => Task.Run(() => { try { service.Decide(owner, c, "approve"); return true; } catch (RuleException) { return false; } })));
        Assert.Single(results, x => x); Assert.Single(service.Claims(owner), c => c.Status == "approved");
    }
    [Fact] public void Failed_transaction_rolls_back()
    {
        Assert.Throws<InvalidOperationException>(() => db.Transaction<int>(() => { db.Execute("UPDATE users SET name='Alterado' WHERE id=@p0", owner); throw new InvalidOperationException(); }));
        Assert.Equal("Pessoa que encontrou", service.Login(new("owner@example.org", "SenhaDeTeste123!")).Name);
    }
    public void Dispose() => db.Dispose();
}
