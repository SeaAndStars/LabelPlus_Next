using System.Security.Cryptography;
using System.Text;

// ============================
// 最小 API 启动入口
// ============================
var builder = WebApplication.CreateBuilder(args);

// 这里可按需添加服务，例如身份认证、日志等
// builder.Services.AddAuthentication();

var app = builder.Build();

// 健康检查/根路径
app.MapGet("/", () => Results.Ok(new { message = "LabelPlus_Next.Server running" }));

// ============================
// 身份认证相关 API
// ============================
// POST /api/auth/login
// 说明：接收用户名和密码，返回包含 token 的响应体
app.MapPost("/api/auth/login", (LoginRequest req) =>
{
    // 在真实项目中，请在此处校验用户名与密码（数据库/外部服务等）
    // 这里为了演示，认为任意非空用户名/密码均视为通过
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.Json(new ApiResponse
        {
            Code = 400,
            Message = "用户名或密码不能为空",
            Data = null
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    // 生成一个简单的演示用 token（请勿在生产环境使用此方式）
    var randomBytes = RandomNumberGenerator.GetBytes(16);
    var raw = Encoding.UTF8.GetBytes($"{req.Username}:{Convert.ToBase64String(randomBytes)}");
    var token = Convert.ToBase64String(raw);

    var resp = new ApiResponse
    {
        Code = 200,
        Message = "登录成功",
        Data = new ApiData { Token = token }
    };

    return Results.Json(resp);
})
.WithName("AuthLogin");

app.Run();

// 为 WebApplicationFactory 提供 Program 类型访问（集成测试需要）
public partial class Program { }

// ============================
// 数据模型
// ============================
/// <summary>
/// 登录请求体
/// </summary>
/// <param name="Username">用户名</param>
/// <param name="Password">密码</param>
public sealed record LoginRequest(string Username, string Password);

/// <summary>
/// 标准响应体
/// </summary>
public sealed class ApiResponse
{
    /// <summary>
    /// 状态码（业务码）
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 数据部分
    /// </summary>
    public ApiData? Data { get; set; }

    /// <summary>
    /// 提示信息
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// 响应中的数据对象
/// </summary>
public sealed class ApiData
{
    /// <summary>
    /// 访问令牌
    /// </summary>
    public string Token { get; set; } = string.Empty;
}
