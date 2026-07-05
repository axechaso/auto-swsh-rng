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
    public void SequenceActionsCannotBeMutatedThroughPublicContract()
    {
        var sequence = new InputSequence([new ResetInputAction()]);

        Assert.Throws<NotSupportedException>(
            () => ((IList<ControllerInputAction>)sequence.Actions)[0] =
                new ButtonInputAction(ControllerButton.A, true));
    }

    [Test]
    public void ControllerActionsRejectUnknownEnums()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ButtonInputAction((ControllerButton)999, true));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DpadInputAction((DpadDirection)999, true));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StickInputAction((ControllerStick)999, 0, 0, true));
        });
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
