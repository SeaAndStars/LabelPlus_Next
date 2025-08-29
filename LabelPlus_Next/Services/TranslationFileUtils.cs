using System.Text;

namespace LabelPlus_Next.Services;

public static class TranslationFileUtils
{
    public static string BuildInitialContent(IEnumerable<string> imageNames)
    {
        var sb = new StringBuilder();
        var nl = Environment.NewLine;
        // Header same as TranslateViewModel.NewTranslationCommand
        sb.Append("1,0"); sb.Append(nl);
        sb.Append("-"); sb.Append(nl);
        sb.Append("框内"); sb.Append(nl);
        sb.Append("框外"); sb.Append(nl);
        sb.Append("-"); sb.Append(nl);
        sb.Append("Default Comment"); sb.Append(nl);
        sb.Append(" You can edit me"); sb.Append(nl);
        sb.Append(nl);
        foreach (var name in imageNames)
        {
            sb.Append(">>>>>>>>["); sb.Append(name); sb.Append("]<<<<<<<"); sb.Append(nl);
            sb.Append(">>>>>>>>["); sb.Append(name); sb.Append("]<<<<<<<<"); sb.Append(nl);
        }
        return sb.ToString();
    }
}
