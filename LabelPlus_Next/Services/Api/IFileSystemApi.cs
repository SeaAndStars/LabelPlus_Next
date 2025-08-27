using System.Threading;
using System.Threading.Tasks;

namespace LabelPlus_Next.Services.Api;

/// <summary>
/// �ļ�ϵͳ��� API ����
/// </summary>
public interface IFileSystemApi
{
    /// <summary>
    /// �г�ָ��·�����ļ����ļ��С�
    /// </summary>
    /// <param name="token">��Ȩ���ƣ����� Bearer xxxxxx ������Ҫ��ĸ�ʽ����</param>
    /// <param name="path">Ҫ�г���·�������� /��/test��</param>
    /// <param name="page">ҳ�룬�� 1 ��ʼ��</param>
    /// <param name="perPage">ÿҳ��С��0 ��ʾ�ɷ���˾����򲻷�ҳ��</param>
    /// <param name="refresh">�Ƿ�ˢ�»��档</param>
    /// <param name="password">Ŀ¼�������루����Ҫ����</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>�����б�����Ԫ���ݡ�</returns>
    Task<FsListResponse> ListAsync(
        string token,
        string path,
        int page = 1,
        int perPage = 0,
        bool refresh = true,
        string password = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ��ȡָ��·���ĵ������飨�ļ���Ŀ¼����
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="path">Ŀ��·�������� /test/a.txt��</param>
    /// <param name="page">ҳ�루����ʵ�ֱ������ֶΣ���</param>
    /// <param name="perPage">ÿҳ��С������ʵ�ֱ������ֶΣ���</param>
    /// <param name="refresh">�Ƿ�ˢ�»��档</param>
    /// <param name="password">�������루����Ҫ����</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>���ظ������ϸ��Ϣ��</returns>
    Task<FsGetResponse> GetAsync(
        string token,
        string path,
        int page = 1,
        int perPage = 0,
        bool refresh = true,
        string password = "",
        CancellationToken cancellationToken = default);
}
