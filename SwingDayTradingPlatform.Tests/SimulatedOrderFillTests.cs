using SwingDayTradingPlatform.Backtesting;
using SwingDayTradingPlatform.Shared;

namespace SwingDayTradingPlatform.Tests;

public class SimulatedOrderFillTests
{
    private static SimulatedOrderFill.PendingOrder MakeLongOrder(
        decimal entry = 5000m, decimal stop = 4990m, decimal? target = 5020m) =>
        new(PositionSide.Long, entry, stop, target, DateTimeOffset.UtcNow, "test");

    private static SimulatedOrderFill.PendingOrder MakeShortOrder(
        decimal entry = 5000m, decimal stop = 5010m, decimal? target = 4980m) =>
        new(PositionSide.Short, entry, stop, target, DateTimeOffset.UtcNow, "test");

    [Fact]
    public void TryFillExit_NeitherHit_ReturnsNull()
    {
        var order = MakeLongOrder(5000m, 4990m, 5020m);
        var bar = TestHelpers.MakeBar(5005, 5015, 4995, 5010); // Low 4995 > Stop 4990, High 5015 < Target 5020
        Assert.Null(SimulatedOrderFill.TryFillExit(order, bar, 0m));
    }

    [Fact]
    public void TryFillExit_Long_StopHit()
    {
        var order = MakeLongOrder(5000m, 4990m, 5020m);
        var bar = TestHelpers.MakeBar(4995, 5005, 4985, 4990, minutesOffset: 5); // Low 4985 < Stop 4990
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
    }

    [Fact]
    public void TryFillExit_Long_TargetHit()
    {
        var order = MakeLongOrder(5000m, 4990m, 5020m);
        var bar = TestHelpers.MakeBar(5010, 5025, 4995, 5022, minutesOffset: 5); // High 5025 > Target 5020
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("Target", result.ExitReason);
        Assert.Equal(5020m, result.FillPrice); // Limit fills at exact price
    }

    [Fact]
    public void TryFillExit_Short_StopHit()
    {
        var order = MakeShortOrder(5000m, 5010m, 4980m);
        var bar = TestHelpers.MakeBar(5005, 5015, 4995, 5012, minutesOffset: 5); // High 5015 > Stop 5010
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
    }

    [Fact]
    public void TryFillExit_Short_TargetHit()
    {
        var order = MakeShortOrder(5000m, 5010m, 4980m);
        var bar = TestHelpers.MakeBar(4990, 5005, 4975, 4978, minutesOffset: 5); // Low 4975 < Target 4980
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("Target", result.ExitReason);
        Assert.Equal(4980m, result.FillPrice);
    }

    [Fact]
    public void TryFillExit_SameBarConflict_StopCloserToOpen_FillsStop()
    {
        // Long: stop at 4990, target at 5020
        var order = MakeLongOrder(5000m, 4990m, 5020m);
        // Bar opens at 4992 (closer to stop 4990 than target 5020), ranges through both
        var bar = TestHelpers.MakeBar(4992, 5025, 4985, 5010, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
    }

    [Fact]
    public void TryFillExit_SameBarConflict_TargetCloserToOpen_FillsTarget()
    {
        // Long: stop at 4980, target at 5010
        var order = MakeLongOrder(5000m, 4980m, 5010m);
        // Bar opens at 5008 (closer to target 5010 than stop 4980), ranges through both
        var bar = TestHelpers.MakeBar(5008, 5015, 4975, 5005, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("Target", result.ExitReason);
    }

    [Fact]
    public void TryFillExit_StopWithSlippage_Long()
    {
        var order = MakeLongOrder(5000m, 4990m, 5050m);
        var bar = TestHelpers.MakeBar(4995, 5005, 4985, 4990, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0.5m);
        Assert.NotNull(result);
        // Stop fill with slippage: max(bar.Low, stop - slippage) = max(4985, 4989.5) = 4989.5
        Assert.Equal(4989.5m, result.FillPrice);
    }

    [Fact]
    public void TryFillExit_StopWithSlippage_Short()
    {
        var order = MakeShortOrder(5000m, 5010m, 4950m);
        var bar = TestHelpers.MakeBar(5005, 5015, 4995, 5012, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0.5m);
        Assert.NotNull(result);
        // Stop fill with slippage: min(bar.High, stop + slippage) = min(5015, 5010.5) = 5010.5
        Assert.Equal(5010.5m, result.FillPrice);
    }

    [Fact]
    public void TryFillExit_GapThrough_Long_FillsAtOpenMinusSlippage()
    {
        var order = MakeLongOrder(5000m, 4990m, 5050m);
        // Bar opens below stop (gap through): Open 4980 < Stop 4990
        var bar = TestHelpers.MakeBar(4980, 4995, 4975, 4985, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0.5m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
        Assert.Equal(4979.5m, result.FillPrice); // 4980 - 0.5
    }

    [Fact]
    public void TryFillExit_GapThrough_Short_FillsAtOpenPlusSlippage()
    {
        var order = MakeShortOrder(5000m, 5010m, 4950m);
        // Bar opens above stop (gap through): Open 5020 > Stop 5010
        var bar = TestHelpers.MakeBar(5020, 5025, 5015, 5022, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0.5m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
        Assert.Equal(5020.5m, result.FillPrice); // 5020 + 0.5
    }

    [Fact]
    public void TryFillExit_NoTarget_OnlyStopChecked()
    {
        var order = new SimulatedOrderFill.PendingOrder(
            PositionSide.Long, 5000m, 4990m, null, DateTimeOffset.UtcNow, "test");
        var bar = TestHelpers.MakeBar(5005, 5050, 4995, 5040, minutesOffset: 5);
        // No target, high doesn't matter for target check
        Assert.Null(SimulatedOrderFill.TryFillExit(order, bar, 0m));
    }

    [Fact]
    public void TryFillExit_NoTarget_StopHit()
    {
        var order = new SimulatedOrderFill.PendingOrder(
            PositionSide.Long, 5000m, 4990m, null, DateTimeOffset.UtcNow, "test");
        var bar = TestHelpers.MakeBar(4995, 5005, 4985, 4990, minutesOffset: 5);
        var result = SimulatedOrderFill.TryFillExit(order, bar, 0m);
        Assert.NotNull(result);
        Assert.Equal("StopLoss", result.ExitReason);
    }
}
