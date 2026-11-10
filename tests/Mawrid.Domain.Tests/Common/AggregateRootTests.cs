using Mawrid.Domain.Common;

namespace Mawrid.Domain.Tests.Common;

public class AggregateRootTests
{
    private sealed record SomethingHappened(Guid Id) : IDomainEvent;

    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void DoSomething() => Raise(new SomethingHappened(Id));
    }

    private sealed class OtherAggregate(Guid id) : AggregateRoot<Guid>(id);

    [Fact]
    public void Raised_events_are_collected_until_cleared()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.DoSomething();
        aggregate.DoSomething();

        aggregate.DomainEvents.Should().HaveCount(2)
            .And.AllBeEquivalentTo(new SomethingHappened(aggregate.Id));

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Entities_with_the_same_type_and_id_are_equal()
    {
        var id = Guid.NewGuid();

        (new TestAggregate(id) == new TestAggregate(id)).Should().BeTrue();
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal_even_with_the_same_id()
    {
        var id = Guid.NewGuid();

        new TestAggregate(id).Equals(new OtherAggregate(id)).Should().BeFalse();
    }

    [Fact]
    public void A_new_aggregate_has_an_empty_row_version()
    {
        new TestAggregate(Guid.NewGuid()).RowVersion.Should().BeEmpty();
    }
}
