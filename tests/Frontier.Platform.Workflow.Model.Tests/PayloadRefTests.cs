using Frontier.Platform.Abstractions;

namespace Frontier.Platform.Workflow.Model.Tests;

public sealed class PayloadRefTests
{
    private const string ValidHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void Validate_WellFormedRef_DoesNotThrow()
    {
        var reference = Sample();

        reference.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("staging/SUB-001/upload/products.xlsx")]
    public void Validate_RelativeStorageUri_Throws(string relativePath)
    {
        var reference = Sample() with { StorageUri = new Uri(relativePath, UriKind.Relative) };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains("storage_uri must be an absolute URI.", exception.Violations);
    }

    [Fact]
    public void Validate_StorageUriWithQueryString_ThrowsSasHygieneViolation()
    {
        var reference = Sample() with
        {
            StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/f.xlsx?sv=2024&sig=abc"),
        };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains(
            "storage_uri must not carry a query string — persisted refs never contain access tokens (ADR-SEC5).",
            exception.Violations);
    }

    [Fact]
    public void Validate_NullContentHash_ThrowsWithoutNullReference()
    {
        var reference = Sample() with { ContentHash = null! };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains("content_hash must be 64 lowercase hex characters (SHA-256).", exception.Violations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0g")]
    public void Validate_MalformedContentHash_Throws(string contentHash)
    {
        var reference = Sample() with { ContentHash = contentHash };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains("content_hash must be 64 lowercase hex characters (SHA-256).", exception.Violations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankContentType_Throws(string contentType)
    {
        var reference = Sample() with { ContentType = contentType };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains("content_type must not be empty.", exception.Violations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveSizeBytes_Throws(long sizeBytes)
    {
        var reference = Sample() with { SizeBytes = sizeBytes };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Contains("size_bytes must be positive.", exception.Violations);
    }

    [Fact]
    public void Validate_EverythingInvalid_ReturnsAllViolations()
    {
        var reference = new PayloadRef
        {
            StorageUri = new Uri("relative/path", UriKind.Relative),
            ContentHash = "short",
            ContentType = " ",
            SizeBytes = 0,
        };

        var exception = Assert.Throws<ContractViolationException>(reference.Validate);

        Assert.Equal(4, exception.Violations.Count);
    }

    private static PayloadRef Sample() => new()
    {
        StorageUri = new Uri("https://frontierstaging.blob.core.windows.net/staging/SUB-001/upload/products.xlsx"),
        ContentHash = ValidHash,
        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        SizeBytes = 1048576,
    };
}
