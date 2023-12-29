using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace VitalRouter.Tests;

class TestSubscriber : ICommandSubscriber
{
    public int Calls { get; private set; }

    public void Receive<T>(T command) where T : ICommand
    {
        Calls++;
    }
}

class TestAsyncSubscriber : IAsyncCommandSubscriber
{
    public int Calls { get; private set; }

    public async UniTask ReceiveAsync<T>(
        T command,
        CancellationToken cancellation = default)
        where T : ICommand
    {
        await Task.Delay(10, cancellation);
        Calls++;
    }
}

class TestInterceptor : IAsyncCommandInterceptor
{
    public int Calls { get; private set; }

    public UniTask InvokeAsync<T>(T command, CancellationToken cancellation, Func<T, CancellationToken, UniTask> next)
        where T : ICommand
    {
        Calls++;
        return next(command, cancellation);
    }
}

class TestStopperInterceptor : IAsyncCommandInterceptor
{
    public UniTask InvokeAsync<T>(T command, CancellationToken cancellation,
        Func<T, CancellationToken, UniTask> next) where T : ICommand
    {
        return UniTask.CompletedTask;
    }
}

struct TestCommand1 : ICommand
{
    public int X;
}

struct TestCommand2 : ICommand
{
    public int X;
}

[TestFixture]
public class CommandBusTest
{
    [Test]
    public async Task Subscribers()
    {
        var commandBus = new CommandBus();

        var subscriber1 = new TestSubscriber();
        var subscriber2 = new TestSubscriber();
        var subscriber3 = new TestAsyncSubscriber();
        var subscriber4 = new TestAsyncSubscriber();

        commandBus.Subscribe(subscriber1);
        commandBus.Subscribe(subscriber2);
        commandBus.Subscribe(subscriber3);
        commandBus.Subscribe(subscriber4);

        await commandBus.PublishAsync(new TestCommand1());
        Assert.That(subscriber1.Calls, Is.EqualTo(1));
        Assert.That(subscriber2.Calls, Is.EqualTo(1));
        Assert.That(subscriber3.Calls, Is.EqualTo(1));
        Assert.That(subscriber4.Calls, Is.EqualTo(1));

        await commandBus.PublishAsync(new TestCommand1());
        Assert.That(subscriber1.Calls, Is.EqualTo(2));
        Assert.That(subscriber2.Calls, Is.EqualTo(2));
        Assert.That(subscriber3.Calls, Is.EqualTo(2));
        Assert.That(subscriber4.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task PropagateInterceptors()
    {
        var commandBus = new CommandBus();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestInterceptor();
        commandBus.Use(interceptor1);
        commandBus.Use(interceptor2);

        await commandBus.PublishAsync(new TestCommand1());

        Assert.That(interceptor1.Calls, Is.EqualTo(1));
        Assert.That(interceptor2.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task StopPropagationByInterceptor()
    {
        var commandBus = new CommandBus();
        var interceptor1 = new TestInterceptor();
        var interceptor2 = new TestStopperInterceptor();
        var subscriber1 = new TestSubscriber();
        commandBus.Use(interceptor1);
        commandBus.Use(interceptor2);
        commandBus.Subscribe(subscriber1);

        await commandBus.PublishAsync(new TestCommand1());

        Assert.That(interceptor1.Calls, Is.EqualTo(1));
        Assert.That(subscriber1.Calls, Is.Zero);
    }
}
