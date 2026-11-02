using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;

namespace PerdeuAlgo;

public sealed class Service(Database db, TimeProvider clock)
{
    private readonly PasswordHasher<string> hasher = new();
    public UserView Register(RegisterInput input) => db.Run(() =>
    {
        var name = Rules.Text(input.Name, "Nome", 2, 60);
        var email = Rules.Text(input.Email, "E-mail", 5, 150).ToLowerInvariant();
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email) throw new RuleException("E-mail inválido.");
        var password = Rules.Text(input.Password, "Senha", 10, 128);
        var hash = hasher.HashPassword(email, password);
        try { return new UserView(db.Insert("INSERT INTO users(name,email,password_hash) VALUES(@p0,@p1,@p2)", name, email, hash), name); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new RuleException("Não foi possível cadastrar este e-mail.", 409); }
    });
    public UserView Login(LoginInput input) => db.Run(() =>
    {
        var email = Rules.Text(input.Email, "E-mail", 5, 150).ToLowerInvariant();
        var password = Rules.Text(input.Password, "Senha", 1, 128);
        var row = db.Query("SELECT id,name,password_hash FROM users WHERE email=@p0", r => new { Id = r.GetInt64(0), Name = r.GetString(1), Hash = r.GetString(2) }, email).SingleOrDefault();
        // A hash is evaluated even for unknown accounts to reduce timing differences.
        var hash = row?.Hash ?? DummyHash;
        if (hasher.VerifyHashedPassword(email, hash, password) == PasswordVerificationResult.Failed || row == null) throw new RuleException("E-mail ou senha incorretos.", 401);
        return new UserView(row.Id, row.Name);
    });
    private static readonly string DummyHash = new PasswordHasher<string>().HashPassword("dummy", Guid.NewGuid().ToString());
    private static Item Map(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), DateOnly.Parse(r.GetString(6)), r.GetString(7), r.GetString(8));
    private const string Columns = "id,owner_id,kind,title,category,location,event_date,description,status";
    public List<Item> Items() => db.Run(() => db.Query($"SELECT {Columns} FROM items ORDER BY id DESC", Map));
    private Item Get(long id) => db.Query($"SELECT {Columns} FROM items WHERE id=@p0", Map, id).SingleOrDefault() ?? throw new RuleException("Registro não encontrado.", 404);
    public Item Create(long user, ItemInput input) => db.Run(() =>
    {
        Rules.Validate(input, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
        var id = db.Insert("INSERT INTO items(owner_id,kind,title,category,location,event_date,description,secret) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7)", user, input.Kind, input.Title.Trim(), input.Category, input.Location.Trim(), input.Date.ToString("yyyy-MM-dd"), input.Description.Trim(), input.Kind == "found" ? input.Secret.Trim() : "");
        return Get(id);
    });
    public List<Match> Matches(long user, long id) => db.Run(() =>
    {
        var item = Get(id); Own(user, item.OwnerId);
        return Items().Select(other => Rules.Compare(item, other)).OfType<Match>().OrderByDescending(m => m.Score).ToList();
    });
    private static void Own(long user, long owner) { if (user != owner) throw new RuleException("Você não tem permissão para esta ação.", 403); }
    public long Request(long user, long itemId, ClaimInput input) => db.Transaction(() =>
    {
        var item = Get(itemId);
        if (item.OwnerId == user) throw new RuleException("Você não pode solicitar seu próprio registro.");
        if (item.Kind != "found" || item.Status != "open") throw new RuleException("Este objeto não está disponível para solicitação.", 409);
        var proof = Rules.Text(input.Proof, "Comprovação", 10, 600);
        try { return db.Insert("INSERT INTO claims(item_id,claimant_id,proof,created_at) VALUES(@p0,@p1,@p2,@p3)", itemId, user, proof, clock.GetUtcNow().ToString("O")); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { throw new RuleException("Você já tem uma solicitação ativa para este objeto.", 409); }
    });
    public List<Claim> Claims(long user) => db.Run(() => db.Query("""
        SELECT c.id,c.item_id,c.claimant_id,u.name,i.title,c.proof,c.status,c.created_at,o.name
        FROM claims c JOIN items i ON i.id=c.item_id JOIN users u ON u.id=c.claimant_id JOIN users o ON o.id=i.owner_id
        WHERE c.claimant_id=@p0 OR i.owner_id=@p0 ORDER BY c.id DESC
        """, r => new Claim(r.GetInt64(0),r.GetInt64(1),r.GetInt64(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)), user));
    public string Secret(long user, long id) => db.Run(() =>
    {
        Own(user, Get(id).OwnerId);
        return db.Query("SELECT secret FROM items WHERE id=@p0", r => r.GetString(0), id).Single();
    });
    public string Decide(long user, long claimId, string action) => db.Transaction(() =>
    {
        var claim = db.Query("SELECT item_id,status FROM claims WHERE id=@p0", r => new { ItemId = r.GetInt64(0), Status = r.GetString(1) }, claimId).SingleOrDefault() ?? throw new RuleException("Solicitação não encontrada.", 404);
        var item = Get(claim.ItemId); Own(user, item.OwnerId);
        if (action == "approve" && claim.Status == "pending" && item.Status == "open")
        {
            db.Execute("UPDATE claims SET status='approved' WHERE id=@p0", claimId);
            db.Execute("UPDATE claims SET status='rejected' WHERE item_id=@p0 AND id<>@p1 AND status='pending'", item.Id, claimId);
            db.Execute("UPDATE items SET status='reserved' WHERE id=@p0", item.Id);
        }
        else if (action == "reject" && claim.Status is "pending" or "approved" && item.Status is "open" or "reserved")
        {
            db.Execute("UPDATE claims SET status='rejected' WHERE id=@p0", claimId);
            if (claim.Status == "approved") db.Execute("UPDATE items SET status='open' WHERE id=@p0", item.Id);
        }
        else if (action == "deliver" && claim.Status == "approved" && item.Status == "reserved")
        {
            db.Execute("UPDATE claims SET status='delivered' WHERE id=@p0", claimId);
            db.Execute("UPDATE items SET status='returned' WHERE id=@p0", item.Id);
        }
        else throw new RuleException("Esta mudança de estado não é permitida.", 409);
        return "Atualizado.";
    });
    public string Cancel(long user, long id) => db.Transaction(() =>
    {
        var item = Get(id); Own(user, item.OwnerId);
        if (item.Status != "open") throw new RuleException("Só é possível encerrar um registro aberto.", 409);
        db.Execute("UPDATE items SET status='cancelled' WHERE id=@p0", id);
        db.Execute("UPDATE claims SET status='rejected' WHERE item_id=@p0 AND status='pending'", id);
        return "Registro encerrado.";
    });
}
