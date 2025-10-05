using System;
using System.Threading.Tasks;
using LabelPlus_Next.DeeplinkClients;

namespace LabelPlus_Next.DeeplinkClients.Sample
{
    // 控制台示例：模拟应用入口接收 uri 并发送 ack
    public class ProgramDeeplinkExample
    {
        public static async Task Main(string[] args)
        {
            string? uriArg = args != null && args.Length > 0 ? args[0] : null;
            if (!string.IsNullOrEmpty(uriArg))
            {
                try
                {
                    var uri = new Uri(uriArg);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var token = query["token"];
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine($"Received token: {token}");
                        var ok = await DeeplinkAckHelper.SendAckWithRetryAsync(token);
                        Console.WriteLine(ok ? "Ack sent" : "Ack queued for later");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("解析 deeplink 失败: " + ex.Message);
                }
            }

            // 演示 flush queued acks（可在应用启动时调用）
            await DeeplinkAckHelper.FlushQueuedAcksAsync();

            Console.WriteLine("Program exiting");
        }
    }
}
