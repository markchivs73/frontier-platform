using Frontier.Platform.Abstractions;
using Microsoft.DurableTask;

namespace Frontier.Platform.Resilience.Tests;

/// <summary>Found with consumer S13.300: the outer retry handler's type test must answer for a type-forwarded BCL exception instead of throwing.</summary>
public sealed class TaskFailureCauseTests
{
    /// <summary>The bug this type fixes: DTF's own test throws for an <see cref="HttpRequestException"/>, which netstandard forwards.</summary>
    [Fact]
    public void IsCausedBy_HttpRequestException_ThrowsInDurableTask()
    {
        var failure = TaskFailureDetails.FromException(new HttpRequestException("connection refused"));

        Assert.Throws<System.Reflection.AmbiguousMatchException>(() => failure.IsCausedBy<ContractViolationException>());
    }

    [Fact]
    public void Is_HttpRequestException_AnswersWithoutThrowing()
    {
        var failure = TaskFailureDetails.FromException(new HttpRequestException("connection refused"));

        Assert.True(TaskFailureCause.Is<HttpRequestException>(failure));
        Assert.False(TaskFailureCause.Is<ContractViolationException>(failure));
    }

    [Fact]
    public void Is_ExactType_IsTrue()
    {
        Assert.True(TaskFailureCause.Is<NetworkPolicyRefusedException>(TaskFailureDetails.FromException(new NetworkPolicyRefusedException("refused"))));
    }

    /// <summary>
    /// The exact-name rule's accepted narrowing: a type derived from an unsealed target is not recognised, because
    /// recognising it would mean resolving the name against whatever the worker has loaded (not replay-safe).
    /// </summary>
    [Fact]
    public void Is_DerivedType_IsFalse()
    {
        var failure = TaskFailureDetails.FromException(new DerivedViolation());

        Assert.False(TaskFailureCause.Is<ContractViolationException>(failure));
    }

    [Fact]
    public void Is_UnknownTypeName_IsFalse()
    {
        var failure = new TaskFailureDetails("No.Such.WrapperException", "outer", null, null, null);

        Assert.False(TaskFailureCause.Is<ContractViolationException>(failure));
    }

    /// <summary>A failure read from the wire can carry an empty type name; it matches nothing and does not throw.</summary>
    [Fact]
    public void Is_EmptyTypeName_IsFalse()
    {
        var failure = new TaskFailureDetails(string.Empty, "no type recorded", null, null, null);

        Assert.False(TaskFailureCause.Is<ContractViolationException>(failure));
        Assert.False(failure.IsCausedBy<ContractViolationException>());
    }

    /// <summary>A refusal that derives from the contract violation, like ModelRoleConfig's.</summary>
    private sealed class DerivedViolation : ContractViolationException
    {
        public DerivedViolation()
            : base("Derived", ["bad"])
        {
        }

        public DerivedViolation(string message)
            : base(message)
        {
        }

        public DerivedViolation(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
