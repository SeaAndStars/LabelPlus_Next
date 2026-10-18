using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LabelPlus_Next;

namespace LabelPlus_Next.Test;

[TestClass]
[DoNotParallelize] // Prevent race conditions on static App state
public class GlobalExceptionTests
{
    private MethodInfo? _handler;

    [TestInitialize]
    public void Init()
    {
#if DEBUG
        Assert.Inconclusive("Debug 构建下 HandleGlobalException 会直接抛出异常，请使用 -c Release 运行这些测试");
#endif
        App.ResetGlobalExceptionTestState();
        _handler = typeof(App).GetMethod("HandleGlobalException", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(_handler, "找不到 HandleGlobalException 方法");
    }

    [TestMethod]
    public void 未处理异常_触发处理_应调用ExitAction()
    {
        bool called = false;
        int exitCode = -1;
        App.IsTestMode = true;
        Assert.IsTrue(App.IsTestMode, "IsTestMode 未成功设置");
        App.ExitAction = c => { called = true; exitCode = c; };

        var app = new App();
        _handler!.Invoke(app, new object[] { new InvalidOperationException("TEST-EX"), false, "Test" });

        Assert.IsTrue(called, "未调用 ExitAction，说明全局异常未被处理");
        Assert.AreEqual(1, exitCode, "退出码应为 1");
    }

    [TestMethod]
    public void 多次异常_只处理一次()
    {
        int invokeCount = 0;
        App.IsTestMode = true;
        App.ExitAction = _ => { invokeCount++; };
        var app = new App();
        _handler!.Invoke(app, new object[] { new Exception("E1"), true, "Test" });
        _handler.Invoke(app, new object[] { new Exception("E2"), true, "Test" });
        Assert.AreEqual(1, invokeCount, "应只处理第一次异常");
    }
}
