using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// ��֤��� API ����
/// </summary>
public interface IAuthApi
{
    /// <summary>
    /// ��¼�ӿڡ�
    /// </summary>
    /// <param name="username">�û�����</param>
    /// <param name="password">���롣</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>���ذ���ҵ���롢��Ϣ�����ݣ����ƣ��ı�׼��Ӧ��</returns>
    Task<ApiResponse<LoginData>> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
}
