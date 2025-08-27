using System.Collections.Generic;
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

    /// <summary>
    /// ����ָ����Ŀ¼�µ����ݡ�
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="parent">��Ŀ¼·�������� /local��</param>
    /// <param name="keywords">�ؼ��֡�</param>
    /// <param name="scope">������Χ���ɷ���˶��壬0 ΪĬ�ϣ���</param>
    /// <param name="page">ҳ�롣</param>
    /// <param name="perPage">ÿҳ��Ŀ������</param>
    /// <param name="password">�������루����Ҫ����</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>�������������</returns>
    Task<FsSearchResponse> SearchAsync(
        string token,
        string parent,
        string keywords,
        int scope = 0,
        int page = 1,
        int perPage = 1,
        string password = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ����Ŀ¼��
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="path">Ҫ������Ŀ��Ŀ¼����·�������� /tt��</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>���ر�׼��Ӧ��code=200 ��ʾ�ɹ���</returns>
    Task<ApiResponse<object>> MkdirAsync(
        string token,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ������Ŀ���� srcDir ���� names ָ�����ļ�/Ŀ¼�� dstDir��
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="srcDir">ԴĿ¼����·�������� /test/a��</param>
    /// <param name="dstDir">Ŀ��Ŀ¼����·�������� /test/b��</param>
    /// <param name="names">Ҫ���Ƶ���Ŀ���Ƽ��ϣ������ srcDir����</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>���ر�׼��Ӧ��</returns>
    Task<ApiResponse<object>> CopyAsync(
        string token,
        string srcDir,
        string dstDir,
        IEnumerable<string> names,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// �ϴ��ļ���ָ��·����PUT /api/fs/put����Ĭ�ϲ��������ϴ��������ϲ㲢�����ơ�
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="filePath">Ŀ���ļ�����·����File-Path ͷ����</param>
    /// <param name="content">�ļ��ֽ����ݡ�</param>
    /// <param name="asTask">�Ƿ���������ʽ�ϴ���As-Task ͷ����Ĭ�� false��</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>����������Ϣ����������</returns>
    Task<FsPutResponse> PutAsync(
        string token,
        string filePath,
        byte[] content,
        bool asTask = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ��ȫ�ϴ�����Զ���Ѵ���ͬ���ļ�������ͨ�� raw_url ����Ϊ��_yyyyMMddHHmmss����׺�����ļ������ϴ��û��ļ������⸲�ǡ�
    /// </summary>
    /// <param name="token">��Ȩ���ơ�</param>
    /// <param name="filePath">Ŀ���ļ�����·����</param>
    /// <param name="content">�û��ļ����ݡ�</param>
    /// <param name="asTask">�Ƿ���������ʽ�ϴ���Ĭ�� false����</param>
    /// <param name="cancellationToken">ȡ�����ơ�</param>
    /// <returns>���������ϴ����û��ļ�������Ӧ��</returns>
    Task<FsPutResponse> SafePutAsync(
        string token,
        string filePath,
        byte[] content,
        bool asTask = false,
        CancellationToken cancellationToken = default);
}
