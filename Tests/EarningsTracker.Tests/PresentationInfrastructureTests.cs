using System.Globalization;
using System.Windows;
using System.Windows.Data;
using EarningsTracker.Commands;
using EarningsTracker.Converters;

namespace EarningsTracker.Tests;

public class PresentationInfrastructureTests
{
    [Fact]
    public void ExistingCommandsRemainExecutableWithoutAPredicate()
    {
        object? received = null;
        var command = new RelayCommand(value => received = value);
        Assert.True(command.CanExecute("parameter"));
        command.Execute("parameter");
        Assert.Equal("parameter", received);
    }

    [Fact]
    public void OptionalPredicateAndExplicitNotificationControlCommandAvailability()
    {
        var available = false; var executed = 0; var notifications = 0;
        var command = new RelayCommand(_ => executed++, value => available && Equals(value, "allowed"));
        command.CanExecuteChanged += (sender, _) => { Assert.Same(command, sender); notifications++; };
        command.Execute("allowed"); Assert.Equal(0, executed);
        available = true; command.RaiseCanExecuteChanged();
        Assert.Equal(1, notifications);
        Assert.True(command.CanExecute("allowed")); Assert.False(command.CanExecute("other"));
        command.Execute("allowed"); Assert.Equal(1, executed);
    }

    [Theory]
    [InlineData("en-US", "1.5")]
    [InlineData("de-DE", "1,5")]
    public void DurationFormattingUsesBindingCulture(string culture, string expected)
    {
        var converter = new MinutesToHoursConverter();
        Assert.Equal(expected, converter.Convert(90m, typeof(string), null!, CultureInfo.GetCultureInfo(culture)));
        Assert.Same(DependencyProperty.UnsetValue, converter.Convert("invalid", typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Same(Binding.DoNothing, converter.ConvertBack("invalid", typeof(decimal), null!, CultureInfo.InvariantCulture));
    }
}
