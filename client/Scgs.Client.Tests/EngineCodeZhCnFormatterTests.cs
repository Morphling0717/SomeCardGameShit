// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class EngineCodeZhCnFormatterTests
{
    [TestMethod]
    public void EveryFrozenEngineCodeHasAStableChineseMessage()
    {
        foreach (EngineCode code in Enum.GetValues<EngineCode>())
        {
            string message = EngineCodeZhCnFormatter.Format(code);
            Assert.IsFalse(string.IsNullOrWhiteSpace(message), code.ToString());
            Assert.DoesNotContain("unknown", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(code.ToString(), message, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StatusFormattingIgnoresNativeDiagnosticAndUnknownCodesRemainActionable()
    {
        var stale = new EngineStatus
        {
            RawCode = (uint)EngineCode.StaleRevision,
            Message = "secret native diagnostic 1234",
        };
        string localized = EngineCodeZhCnFormatter.Format(stale);
        Assert.DoesNotContain("secret", localized, StringComparison.Ordinal);
        StringAssert.Contains(localized, "重新选择");

        Assert.AreEqual("未知规则错误（代码 9001）。", EngineCodeZhCnFormatter.Format(9001U));
    }
}
