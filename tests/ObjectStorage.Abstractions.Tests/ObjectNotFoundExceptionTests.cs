using Forge.ObjectStorage;
using Shouldly;

namespace Forge.ObjectStorage.Abstractions.Tests;

public sealed class ObjectNotFoundExceptionTests
{
    [Fact]
    public void ObjectKey_is_set_from_constructor()
    {
        var ex = new ObjectNotFoundException("my-key");
        ex.ObjectKey.ShouldBe("my-key");
    }

    [Fact]
    public void Message_contains_object_key()
    {
        var ex = new ObjectNotFoundException("some/path/key");
        ex.Message.ShouldContain("some/path/key");
    }

    [Fact]
    public void Is_assignable_to_Exception()
    {
        var ex = new ObjectNotFoundException("k");
        (ex is Exception).ShouldBeTrue();
    }
}
