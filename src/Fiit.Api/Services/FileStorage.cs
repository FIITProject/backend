using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Fiit.Api.Services;

public interface IFileStorage
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct);
    Task<byte[]> GetAsync(string key, CancellationToken ct);
}

public class StorageOptions
{
    public const string Section = "Storage";

    public required string ServiceUrl { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public required string Bucket { get; init; }
    public string Region { get; init; } = "ru-central1";
    /// <summary>Создать бакет при старте, если его нет (локальная разработка с MinIO).</summary>
    public bool CreateBucket { get; init; }
}

/// <summary>Приватный бакет Yandex Object Storage (локально — MinIO). Файлы отдаются только через API с проверкой прав.</summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly StorageOptions _o;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        _o = options.Value;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(_o.AccessKey, _o.SecretKey), new AmazonS3Config
        {
            ServiceURL = _o.ServiceUrl,
            AuthenticationRegion = _o.Region,
            ForcePathStyle = true,
            // Yandex Object Storage и MinIO не везде поддерживают новые чексуммы SDK по умолчанию
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!_o.CreateBucket)
            return;
        var buckets = await _s3.ListBucketsAsync(ct);
        if (buckets.Buckets?.Any(b => b.BucketName == _o.Bucket) != true)
            await _s3.PutBucketAsync(_o.Bucket, ct);
    }

    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
        }, ct);
    }

    public async Task<byte[]> GetAsync(string key, CancellationToken ct)
    {
        using var response = await _s3.GetObjectAsync(_o.Bucket, key, ct);
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public void Dispose() => _s3.Dispose();
}
