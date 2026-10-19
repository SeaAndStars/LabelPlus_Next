using LabelPlus_Next.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize]
public class UiTextTests
{
    [TestCleanup]
    public void Cleanup() => LocalizationService.Apply("default");

    [TestMethod]
    public void CodeMessagesFollowSelectedLanguage()
    {
        LocalizationService.Apply("zh-CN");
        Assert.AreEqual("提示", UiText.Tr("提示"));
        Assert.AreEqual("已选择 3 个文件。", UiText.Tr("已选择 {0} 个文件。", 3));

        LocalizationService.Apply("en");
        Assert.AreEqual("Notice", UiText.Tr("提示"));
        Assert.AreEqual("Saved: demo.txt", UiText.Tr("保存成功：{0}", "demo.txt"));

        LocalizationService.Apply("zh-hant-tw");
        Assert.AreEqual("錯誤", UiText.Tr("错误"));
        Assert.AreEqual("儲存成功：demo.txt", UiText.Tr("保存成功：{0}", "demo.txt"));
    }
}
