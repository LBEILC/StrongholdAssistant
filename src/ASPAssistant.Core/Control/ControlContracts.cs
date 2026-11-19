using System.Text.Json.Serialization;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Control;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(BuyAction), "buy")]
[JsonDerivedType(typeof(SellAction), "sell")]
[JsonDerivedType(typeof(RefreshAction), "refresh")]
[JsonDerivedType(typeof(FreezeAction), "freeze")]
[JsonDerivedType(typeof(UpgradeAction), "upgrade")]
[JsonDerivedType(typeof(MoveAction), "move")]
[JsonDerivedType(typeof(FaceAction), "face")]
[JsonDerivedType(typeof(ReadyAction), "ready")]
[JsonDerivedType(typeof(PickStrategyAction), "pickStrategy")]
[JsonDerivedType(typeof(PickAdaptAction), "pickAdapt")]
[JsonDerivedType(typeof(PickCustomAction), "pickCustom")]
public abstract record GameAction;
public sealed record BuyAction(int Slot, string? Expect = null) : GameAction;
public sealed record SellAction(FieldCell From, string? Expect = null) : GameAction;
public sealed record RefreshAction : GameAction;
public sealed record FreezeAction(bool On) : GameAction;
public sealed record UpgradeAction : GameAction;
public sealed record MoveAction(FieldCell From, FieldCell To, string? Facing = null, string? Expect = null) : GameAction;
public sealed record FaceAction(FieldCell At, string Facing) : GameAction;
public sealed record ReadyAction(bool On = true) : GameAction;
public sealed record PickStrategyAction(string StrategyId) : GameAction;
public sealed record PickAdaptAction(int Index) : GameAction;
public sealed record PickCustomAction(int Tier, string[] Operators) : GameAction;
public abstract record InputStep;
public sealed record TapStep(double X, double Y) : InputStep;
public sealed record DragStep(double X1, double Y1, double X2, double Y2, int HoldMs, int MoveMs, int ReleaseMs) : InputStep;
public sealed record KeyStep(string Key) : InputStep;
public sealed record WaitStep(int Ms) : InputStep;
public sealed record ControlFrame(byte[] Image, int Width, int Height, FieldState? Field, MatchState? State, bool Keyboard);
public sealed record CompiledAction(GameAction Action, string Text, IReadOnlyList<InputStep> Steps, int SettleMs);
public sealed record CompileResult(CompiledAction? Plan, string? Reason, string? Problem);
public sealed record VerifyResult(bool? Ok, string? Problem);
public interface IActionCompiler
{
    CompileResult Compile(GameAction action, ControlFrame now);
    VerifyResult Verify(GameAction action, ControlFrame before, ControlFrame after);
}
public interface IGameInput
{
    Task RunAsync(InputStep step, CancellationToken token);
}
