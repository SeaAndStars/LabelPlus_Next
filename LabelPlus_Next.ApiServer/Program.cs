using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using BCrypt.Net;
using LabelPlus_Next.ApiServer.Data;
using LabelPlus_Next.ApiServer.Entities;
using LabelPlus_Next.ApiServer.Models;
using LabelPlus_Next.ApiServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Reflection;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

var startupDbFailures = new List<Exception>();

var cs = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(cs))
{
    try
    {
        builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(cs));
    }
    catch (Exception ex)
    {
        startupDbFailures.Add(ex);
        builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("labelplus"));
    }
}
else
{
    builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("labelplus"));
}

builder.Services.AddSingleton<IJwtService, JwtService>();

var app = builder.Build();
var logger = app.Logger;

foreach (var ex in startupDbFailures)
{
    logger.LogWarning(ex, "Failed to configure PostgreSQL. Falling back to in-memory database.");
}

// Ensure storage folders
var storage = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
Directory.CreateDirectory(storage.RootPath);
Directory.CreateDirectory(storage.TempPath);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to ensure database is created");
        throw;
    }
    if (!await db.Users.AnyAsync())
    {
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            BasePath = "/",
            Disabled = false,
            Permission = 0,
            Role = 0,
            SsoId = null,
            Otp = false
        });
        await db.SaveChangesAsync();
    }
}



static string NormalizePath(string? p)
{
    if (string.IsNullOrWhiteSpace(p)) return "/";
    p = p.Replace('\\', '/');
    if (!p.StartsWith('/')) p = "/" + p;
    while (p.Contains("//")) p = p.Replace("//", "/");
    // prevent traversal
    if (p.Contains("..")) p = "/" + string.Join('/', p.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".."));
    if (!p.StartsWith('/')) p = "/" + p;
    return p;
}

static string GetParent(string path)
{
    if (string.IsNullOrWhiteSpace(path) || path == "/") return "/";
    var idx = path.LastIndexOf('/');
    if (idx < 0) return "/";
    return idx == 0 ? "/" : path.Substring(0, idx);
}

static string GetName(string path)
{
    if (string.IsNullOrWhiteSpace(path) || path == "/") return string.Empty;
    var idx = path.LastIndexOf('/');
    return idx < 0 ? path : path[(idx + 1)..];
}

static string ToIso(DateTimeOffset ts) => ts.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

static string MapToPhysical(string path, StorageOptions opt)
{
    var rel = path.Trim('/').Replace('/', Path.DirectorySeparatorChar);
    var full = Path.GetFullPath(Path.Combine(opt.RootPath, rel));
    var root = Path.GetFullPath(opt.RootPath);
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("invalid path");
    return full;
}

static FsItem ToItem(FileEntry f) => new()
{
    Created = ToIso(f.Created),
    Modified = ToIso(f.Modified),
    IsDir = f.IsDir,
    Name = f.Name,
    Size = f.Size,
    Thumb = f.Thumb,
    Hashinfo = f.Hashinfo,
    Sign = f.Sign,
    Type = f.Type
};

static string? GetTokenFromAuth(HttpRequest req)
{
    var auth = req.Headers["Authorization"].ToString();
    if (string.IsNullOrWhiteSpace(auth)) return null;
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth.Substring(7).Trim();
    return auth.Trim();
}

var api = app.MapGroup("/api");

api.MapPost("/auth/login", async (LoginRequest req, AppDbContext db, IJwtService jwt) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Username))
        return Results.Json(new ApiResponse<LoginData> { Code = 400, Message = "bad request" });

    if (req.Username.StartsWith("sso:", StringComparison.OrdinalIgnoreCase))
    {
        var sid = req.Username[4..];
        var user = await db.Users.FirstOrDefaultAsync(u => u.SsoId == sid);
        if (user is null)
        {
            user = new User
            {
                Username = $"sso_{sid}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                BasePath = "/",
                Disabled = false,
                Permission = 0,
                Role = 0,
                SsoId = sid,
                Otp = false
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        var token2 = jwt.IssueToken(user.Id, user.Username, user.SsoId);
        return Results.Json(new ApiResponse<LoginData> { Code = 200, Data = new LoginData { Token = token2 }, Message = "ok" });
    }

    var u = await db.Users.FirstOrDefaultAsync(x => x.Username == req.Username);
    if (u is null || string.IsNullOrWhiteSpace(req.Password) || !BCrypt.Net.BCrypt.Verify(req.Password, u.PasswordHash))
        return Results.Json(new ApiResponse<LoginData> { Code = 401, Message = "unauthorized" });

    var token = jwt.IssueToken(u.Id, u.Username, u.SsoId);
    return Results.Json(new ApiResponse<LoginData> { Code = 200, Data = new LoginData { Token = token }, Message = "ok" });
});

// SSO exchange (placeholder)
api.MapPost("/auth/sso/exchange", async (Dictionary<string, string> req, AppDbContext db, IJwtService jwt) =>
{
    if (!req.TryGetValue("provider", out var provider) || !req.TryGetValue("code", out var code))
        return Results.Json(new ApiResponse<LoginData> { Code = 400, Message = "bad request" });
    var sid = $"{provider}:{code}";
    var user = await db.Users.FirstOrDefaultAsync(u => u.SsoId == sid);
    if (user is null)
    {
        user = new User
        {
            Username = $"sso_{provider}_{Guid.NewGuid():N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            BasePath = "/",
            Disabled = false,
            Permission = 0,
            Role = 0,
            SsoId = sid,
            Otp = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
    var token = jwt.IssueToken(user.Id, user.Username, user.SsoId);
    return Results.Json(new ApiResponse<LoginData> { Code = 200, Data = new LoginData { Token = token }, Message = "ok" });
});

api.MapPost("/auth/sso", async (Dictionary<string, string> req, AppDbContext db, IJwtService jwt) =>
{
    if (!req.TryGetValue("sso_id", out var sid) || string.IsNullOrWhiteSpace(sid))
        return Results.Json(new ApiResponse<LoginData> { Code = 400, Message = "bad request" });
    var user = await db.Users.FirstOrDefaultAsync(u => u.SsoId == sid);
    if (user is null)
    {
        user = new User
        {
            Username = $"sso_{sid}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            BasePath = "/",
            Disabled = false,
            Permission = 0,
            Role = 0,
            SsoId = sid,
            Otp = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
    var token = jwt.IssueToken(user.Id, user.Username, user.SsoId);
    return Results.Json(new ApiResponse<LoginData> { Code = 200, Data = new LoginData { Token = token }, Message = "ok" });
});

api.MapGet("/me", async (HttpRequest request, AppDbContext db, IJwtService jwt) =>
{
    var token = GetTokenFromAuth(request);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Json(new ApiResponse<MeData> { Code = 401, Message = "unauthorized" });
    var principal = jwt.ValidateToken(token);
    if (principal is null)
        return Results.Json(new ApiResponse<MeData> { Code = 401, Message = "unauthorized" });
    var uidStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!long.TryParse(uidStr, out var uid))
        return Results.Json(new ApiResponse<MeData> { Code = 401, Message = "unauthorized" });
    var u = await db.Users.FindAsync(uid);
    if (u is null)
        return Results.Json(new ApiResponse<MeData> { Code = 404, Message = "not found" });

    var me = new MeData
    {
        BasePath = u.BasePath,
        Disabled = u.Disabled,
        Id = u.Id,
        Otp = u.Otp,
        Password = null,
        Permission = u.Permission,
        Role = u.Role,
        SsoId = u.SsoId,
        Username = u.Username
    };
    return Results.Json(new ApiResponse<MeData> { Code = 200, Data = me, Message = "ok" });
});

var fs = api.MapGroup("/fs");

fs.MapPost("/list", async (FsListRequest req, HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new FsListResponse { Code = 401, Message = "unauthorized" });

    var path = NormalizePath(req.Path);
    var query = db.Files.AsNoTracking().Where(f => f.ParentPath == path).OrderBy(f => f.IsDir ? 0 : 1).ThenBy(f => f.Name);
    var items = await query.ToListAsync();
    var data = new FsListData
    {
        Content = items.Select(ToItem).ToArray(),
        Header = null,
        Provider = "labelplus-db",
        Readme = null,
        Total = items.LongCount(),
        Write = true
    };
    return Results.Json(new FsListResponse { Code = 200, Data = data, Message = "ok" });
});

fs.MapPost("/get", async (FsGetRequest req, HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new FsGetResponse { Code = 401, Message = "unauthorized" });

    var path = NormalizePath(req.Path);
    var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Path == path);
    if (f is null)
        return Results.Json(new FsGetResponse { Code = 404, Message = "not found" });
    var data = new FsGetData
    {
        Created = ToIso(f.Created),
        Modified = ToIso(f.Modified),
        IsDir = f.IsDir,
        Name = f.Name,
        Provider = "labelplus-db",
        RawUrl = f.IsDir ? null : $"/api/fs/raw?path={Uri.EscapeDataString(f.Path)}",
        Readme = null,
        Related = null,
        Sign = f.Sign,
        Size = f.Size,
        Thumb = f.Thumb,
        Type = f.Type,
        Hashinfo = f.Hashinfo
    };
    return Results.Json(new FsGetResponse { Code = 200, Data = data, Message = "ok" });
});

fs.MapPost("/search", async (FsSearchRequest req, HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new FsSearchResponse { Code = 401, Message = "unauthorized" });

    var parent = NormalizePath(req.Parent);
    var kw = (req.Keywords ?? string.Empty).Trim();
    var q = db.Files.AsNoTracking().Where(f => f.ParentPath.StartsWith(parent));
    if (!string.IsNullOrWhiteSpace(kw)) q = q.Where(f => f.Name.Contains(kw));
    var list = await q.Take(Math.Max(1, req.PerPage)).ToListAsync();
    var data = new FsSearchData
    {
        Content = list.Select(f => new FsSearchItem
        {
            IsDir = f.IsDir,
            Name = f.Name,
            Parent = f.ParentPath,
            Size = f.Size,
            Type = f.Type
        }).ToArray(),
        Total = await q.LongCountAsync()
    };
    return Results.Json(new FsSearchResponse { Code = 200, Data = data, Message = "ok" });
});

fs.MapPost("/mkdir", async (MkdirRequest req, HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new ApiResponse<object> { Code = 401, Message = "unauthorized" });

    var path = NormalizePath(req.Path);
    if (path == "/") return Results.Json(new ApiResponse<object> { Code = 409, Message = "already exists" });
    var exists = await db.Files.AnyAsync(f => f.Path == path);
    if (exists) return Results.Json(new ApiResponse<object> { Code = 409, Message = "already exists" });

    // persist to disk
    try
    {
        var phys = MapToPhysical(path, app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value);
        Directory.CreateDirectory(phys);
    }
    catch (IOException ex)
    {
        logger.LogWarning(ex, "Failed to create directory on disk for {Path}", path);
    }
    catch (UnauthorizedAccessException ex)
    {
        logger.LogWarning(ex, "Unauthorized to create directory on disk for {Path}", path);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error creating directory for {Path}", path);
        throw;
    }

    var entry = new FileEntry
    {
        Path = path,
        ParentPath = GetParent(path),
        Name = GetName(path),
        IsDir = true,
        Size = 0,
        Created = DateTimeOffset.UtcNow,
        Modified = DateTimeOffset.UtcNow
    };
    db.Files.Add(entry);
    await db.SaveChangesAsync();
    return Results.Json(new ApiResponse<object> { Code = 200, Message = "ok" });
});

fs.MapPost("/copy", async (CopyRequest req, HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new ApiResponse<object> { Code = 401, Message = "unauthorized" });

    var srcDir = NormalizePath(req.SrcDir);
    var dstDir = NormalizePath(req.DstDir);
    if (string.IsNullOrWhiteSpace(srcDir) || string.IsNullOrWhiteSpace(dstDir) || req.Names is null)
        return Results.Json(new ApiResponse<object> { Code = 400, Message = "bad request" });

    var opt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;

    foreach (var name in req.Names)
    {
        var sPath = NormalizePath(srcDir + "/" + name);
        var src = await db.Files.FirstOrDefaultAsync(f => f.Path == sPath);
        if (src is null) continue;
        var dPath = NormalizePath(dstDir + "/" + name);
        var exists = await db.Files.AnyAsync(f => f.Path == dPath);
        if (exists) continue;

        if (src.IsDir)
        {
            // create directory on disk & db
            try { Directory.CreateDirectory(MapToPhysical(dPath, opt)); }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to create destination directory {Path}", dPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Unauthorized creating destination directory {Path}", dPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating destination directory {Path}", dPath);
                throw;
            }
            db.Files.Add(new FileEntry
            {
                Path = dPath,
                ParentPath = GetParent(dPath),
                Name = GetName(dPath),
                IsDir = true,
                Size = 0,
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // file copy
            try
            {
                var srcFile = MapToPhysical(sPath, opt);
                var dstFile = MapToPhysical(dPath, opt);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                File.Copy(srcFile, dstFile, overwrite: false);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "File copy failed from {Source} to {Destination}", sPath, dPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Unauthorized copying file from {Source} to {Destination}", sPath, dPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error copying file from {Source} to {Destination}", sPath, dPath);
                throw;
            }
            db.Files.Add(new FileEntry
            {
                Path = dPath,
                ParentPath = GetParent(dPath),
                Name = GetName(dPath),
                IsDir = false,
                Size = src.Size,
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
                Provider = src.Provider,
                Thumb = src.Thumb,
                Hashinfo = src.Hashinfo,
                Sign = src.Sign,
                Type = src.Type
            });
        }
    }
    await db.SaveChangesAsync();
    return Results.Json(new ApiResponse<object> { Code = 200, Message = "ok" });
});

// Upload with chunk support
fs.MapMethods("/put", new[] { "PUT" }, async (HttpRequest http, AppDbContext db) =>
{
    var token = GetTokenFromAuth(http);
    if (string.IsNullOrWhiteSpace(token)) return Results.Json(new FsPutResponse { Code = 401, Message = "unauthorized" });

    var pathHeader = http.Headers["File-Path"].ToString();
    var path = NormalizePath(Uri.UnescapeDataString(pathHeader ?? "/"));

    var opt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
    var phys = MapToPhysical(path, opt);
    Directory.CreateDirectory(Path.GetDirectoryName(phys)!);

    var uploadId = http.Headers["Upload-Id"].ToString();
    var chunkIndexStr = http.Headers["Chunk-Index"].ToString();
    var chunkTotalStr = http.Headers["Chunk-Total"].ToString();

    if (!string.IsNullOrWhiteSpace(uploadId) && int.TryParse(chunkIndexStr, out var cidx) && int.TryParse(chunkTotalStr, out var ctot) && ctot > 0)
    {
        var tmpDir = Path.Combine(opt.TempPath, uploadId);
        Directory.CreateDirectory(tmpDir);
        var partFile = Path.Combine(tmpDir, $"part-{cidx:D08}.bin");
        await using (var fsOut = new FileStream(partFile, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
        {
            await http.Body.CopyToAsync(fsOut);
        }

        var parts = Directory.GetFiles(tmpDir, "part-*.bin");
        if (parts.Length >= ctot)
        {
            // combine
            var ordered = parts.OrderBy(p => p).ToList();
            await using (var fsFinal = new FileStream(phys, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
            {
                foreach (var pf in ordered)
                {
                    await using var src = new FileStream(pf, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, useAsync: true);
                    await src.CopyToAsync(fsFinal);
                }
            }
            try { Directory.Delete(tmpDir, true); }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to delete temporary upload directory {TmpDir}", tmpDir);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Unauthorized deleting temporary upload directory {TmpDir}", tmpDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error deleting temporary upload directory {TmpDir}", tmpDir);
                throw;
            }

            // update db
            var now = DateTimeOffset.UtcNow;
            var fi = new FileInfo(phys);
            var existing = await db.Files.FirstOrDefaultAsync(f => f.Path == path);
            if (existing is null)
            {
                db.Files.Add(new FileEntry
                {
                    Path = path,
                    ParentPath = GetParent(path),
                    Name = GetName(path),
                    IsDir = false,
                    Size = fi.Exists ? fi.Length : 0,
                    Created = now,
                    Modified = now
                });
            }
            else
            {
                existing.IsDir = false;
                existing.Size = fi.Exists ? fi.Length : 0;
                existing.Modified = now;
            }
            await db.SaveChangesAsync();
        }
    }
    else
    {
        // normal upload
        await using (var fsOut = new FileStream(phys, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024, useAsync: true))
        {
            await http.Body.CopyToAsync(fsOut);
        }

        var now = DateTimeOffset.UtcNow;
        var fi = new FileInfo(phys);
        var existing = await db.Files.FirstOrDefaultAsync(f => f.Path == path);
        if (existing is null)
        {
            db.Files.Add(new FileEntry
            {
                Path = path,
                ParentPath = GetParent(path),
                Name = GetName(path),
                IsDir = false,
                Size = fi.Exists ? fi.Length : 0,
                Created = now,
                Modified = now
            });
        }
        else
        {
            existing.IsDir = false;
            existing.Size = fi.Exists ? fi.Length : 0;
            existing.Modified = now;
        }
        await db.SaveChangesAsync();
    }

    var task = new FsTask
    {
        Error = null,
        Id = Guid.NewGuid().ToString("N"),
        Name = GetName(path),
        Progress = 100,
        State = 1,
        Status = "success"
    };
    return Results.Json(new FsPutResponse { Code = 200, Data = new FsPutData { Task = task }, Message = "ok" });
});

fs.MapGet("/raw", async (string path, HttpContext ctx, AppDbContext db, IJwtService jwt) =>
{
    var token = GetTokenFromAuth(ctx.Request);
    if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();
    if (jwt.ValidateToken(token) is null) return Results.Unauthorized();

    path = NormalizePath(path);
    var opt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
    var phys = MapToPhysical(path, opt);
    if (System.IO.File.Exists(phys))
    {
        return Results.File(phys, contentType: "application/octet-stream", fileDownloadName: Path.GetFileName(phys));
    }
    var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Path == path && !x.IsDir);
    if (f is null || f.Content is null) return Results.NotFound();
    return Results.File(f.Content, contentType: "application/octet-stream", fileDownloadName: f.Name);
});

app.Run();

[JsonSerializable(typeof(ApiResponse<LoginData>))]
[JsonSerializable(typeof(ApiResponse<MeData>))]
[JsonSerializable(typeof(FsListResponse))]
[JsonSerializable(typeof(FsGetResponse))]
[JsonSerializable(typeof(FsSearchResponse))]
[JsonSerializable(typeof(ApiResponse<object>))]
[JsonSerializable(typeof(FsPutResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }
