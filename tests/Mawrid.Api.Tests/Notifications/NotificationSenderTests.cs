using Mawrid.Application.Common.Notifications;
using Mawrid.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mawrid.Api.Tests.Notifications;

public sealed class NotificationSenderTests
{
    private static readonly UserNotification Hello = new("test.hello", "Hello", "Hello there");

    [Fact]
    public async Task A_failing_channel_does_not_stop_the_others()
    {
        var working = new FakeChannel("working");
        var sender = new NotificationSender([new FakeChannel("broken", fail: true), working], NullLogger<NotificationSender>.Instance);

        await sender.SendAsync([Guid.NewGuid()], Hello, CancellationToken.None);

        working.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_recipients_are_removed_and_an_empty_list_sends_nothing()
    {
        var channel = new FakeChannel("only");
        var sender = new NotificationSender([channel], NullLogger<NotificationSender>.Instance);
        var user = Guid.NewGuid();

        await sender.SendAsync([], Hello, CancellationToken.None);
        await sender.SendAsync([user, user], Hello, CancellationToken.None);

        channel.Calls.Should().Be(1);
        channel.LastRecipients.Should().Equal(user);
    }

    private sealed class FakeChannel(string name, bool fail = false) : INotificationChannel
    {
        public string Name => name;

        public int Calls { get; private set; }

        public IReadOnlyCollection<Guid> LastRecipients { get; private set; } = [];

        public Task SendAsync(IReadOnlyCollection<Guid> userIds, UserNotification notification, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidOperationException("Channel is down.");
            }

            Calls++;
            LastRecipients = userIds;
            return Task.CompletedTask;
        }
    }
}
