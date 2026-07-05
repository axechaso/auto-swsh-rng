using AutoSwshRng.Core.Automation;

namespace AutoSwshRng.Core.Tests.Automation;

public class InputSequenceContractsTests
{
    [Test]
    public void SequenceCopiesActions()
    {
        ControllerInputAction[] actions =
        [
            new ButtonInputAction(ControllerButton.A, true),
            new WaitInputAction(TimeSpan.FromMilliseconds(10)),
        ];
        var sequence = new InputSequence(actions);

        actions[0] = new ResetInputAction();

        Assert.That(sequence.Actions[0], Is.TypeOf<ButtonInputAction>());
    }

    [Test]
    public void WaitMustBeNonNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WaitInputAction(TimeSpan.FromMilliseconds(-1)));
    }

    [Test]
    public void ConnectionRequiresPort()
    {
        Assert.Throws<ArgumentException>(() => new ControllerConnectionRequest(" "));
    }
}
