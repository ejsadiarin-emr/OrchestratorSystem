using System.Net;
using System.Net.Http.Json;
using DeploymentPoC.Orchestrator.IntegrationTests.Infrastructure;
using NUnit.Framework;

namespace DeploymentPoC.Orchestrator.IntegrationTests.Api;

public sealed class WorkloadsApiContractTests
{
    [Test]
    public async Task WorkloadsApi_CreateRevisionPublishAndList_HappyPath()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var pkgA = await CreatePackageAsync(client, "w1-workload-pkg-a");
        var pkgB = await CreatePackageAsync(client, "w1-workload-pkg-b");

        var createWorkloadResponse = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = "voice-stack",
            description = "canonical workload"
        });
        Assert.That(createWorkloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createdWorkload = await createWorkloadResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(createdWorkload, Is.Not.Null);

        var createRevisionResponse = await client.PostAsJsonAsync($"/api/workloads/{createdWorkload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 },
                new { packageId = pkgB, packageIndex = 2 }
            }
        });
        Assert.That(createRevisionResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createdRevision = await createRevisionResponse.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(createdRevision, Is.Not.Null);

        var publishResponse = await client.PostAsJsonAsync($"/api/workloads/{createdWorkload.WorkloadId}/publish", new
        {
            revisionId = createdRevision!.RevisionId
        });
        Assert.That(publishResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var publishedDetail = await publishResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(publishedDetail, Is.Not.Null);
        Assert.That(publishedDetail!.PublishedRevisionId, Is.EqualTo(createdRevision.RevisionId));

        var getListResponse = await client.GetAsync("/api/workloads");
        Assert.That(getListResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var list = await getListResponse.Content.ReadFromJsonAsync<WorkloadListResponse>();
        Assert.That(list, Is.Not.Null);
        Assert.That(list!.Workloads.Any(w => w.WorkloadId == createdWorkload.WorkloadId), Is.True);

        var getDetailResponse = await client.GetAsync($"/api/workloads/{createdWorkload.WorkloadId}");
        Assert.That(getDetailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await getDetailResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(detail, Is.Not.Null);
        Assert.That(detail!.Revisions.Count, Is.EqualTo(1));
        Assert.That(detail.Revisions[0].Packages.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task WorkloadsApi_CreateRevision_WithWrongPackageCount_ReturnsDeterministicValidationPayload()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var pkgA = await CreatePackageAsync(client, "w1-workload-pkg-single");

        var createWorkloadResponse = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = "invalid-workload"
        });
        createWorkloadResponse.EnsureSuccessStatusCode();
        var workload = await createWorkloadResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var createRevisionResponse = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 }
            }
        });

        Assert.That(createRevisionResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await createRevisionResponse.Content.ReadFromJsonAsync<ValidationErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo("validation_failed"));
        Assert.That(error.Message, Is.EqualTo("Validation failed"));
        Assert.That(error.Errors.Any(e => e.Field == "packages"), Is.True);
    }

    [Test]
    public async Task WorkloadsApi_Publish_WithReplacePublishedFalse_KeepsOtherRevisionsPublished()
    {
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var pkgA = await CreatePackageAsync(client, "multi-pub-pkg-a");
        var pkgB = await CreatePackageAsync(client, "multi-pub-pkg-b");

        var createWorkloadResponse = await client.PostAsJsonAsync("/api/workloads", new
        {
            name = "multi-publish-workload",
            description = "test"
        });
        Assert.That(createWorkloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var workload = await createWorkloadResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(workload, Is.Not.Null);

        var rev1Response = await client.PostAsJsonAsync($"/api/workloads/{workload!.WorkloadId}/revisions", new
        {
            version = "1.0.0",
            packages = new[]
            {
                new { packageId = pkgA, packageIndex = 1 }
            }
        });
        rev1Response.EnsureSuccessStatusCode();
        var rev1 = await rev1Response.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev1, Is.Not.Null);

        var rev2Response = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/revisions", new
        {
            version = "2.0.0",
            packages = new[]
            {
                new { packageId = pkgB, packageIndex = 1 }
            }
        });
        rev2Response.EnsureSuccessStatusCode();
        var rev2 = await rev2Response.Content.ReadFromJsonAsync<WorkloadRevisionResponse>();
        Assert.That(rev2, Is.Not.Null);

        var publish1 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev1!.RevisionId,
            replacePublished = true
        });
        publish1.EnsureSuccessStatusCode();

        var publish2 = await client.PostAsJsonAsync($"/api/workloads/{workload.WorkloadId}/publish", new
        {
            revisionId = rev2!.RevisionId,
            replacePublished = false
        });
        publish2.EnsureSuccessStatusCode();

        var detailResponse = await client.GetAsync($"/api/workloads/{workload.WorkloadId}");
        Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await detailResponse.Content.ReadFromJsonAsync<WorkloadDetailResponse>();
        Assert.That(detail, Is.Not.Null);

        var publishedRevisions = detail!.Revisions.Where(r => r.IsPublished).ToList();
        Assert.That(publishedRevisions.Count, Is.EqualTo(2));
    }

    private static async Task<Guid> CreatePackageAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/packages", new
        {
            name,
            version = "1.0.0",
            sourcePath = $"/tmp/{name}",
            installType = "archive",
            installArgs = ""
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PackageResponse>();
        Assert.That(body, Is.Not.Null);
        return body!.Id;
    }

    private sealed class PackageResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class WorkloadListResponse
    {
        public List<WorkloadSummaryResponse> Workloads { get; set; } = new();
    }

    private sealed class WorkloadSummaryResponse
    {
        public Guid WorkloadId { get; set; }
    }

    private sealed class WorkloadDetailResponse
    {
        public Guid WorkloadId { get; set; }
        public Guid? PublishedRevisionId { get; set; }
        public List<WorkloadRevisionResponse> Revisions { get; set; } = new();
    }

    private sealed class WorkloadRevisionResponse
    {
        public Guid RevisionId { get; set; }
        public bool IsPublished { get; set; }
        public List<WorkloadPackageResponse> Packages { get; set; } = new();
    }

    private sealed class WorkloadPackageResponse
    {
        public Guid PackageId { get; set; }
        public int PackageIndex { get; set; }
    }

    private sealed class ValidationErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<ValidationFieldError> Errors { get; set; } = new();
    }

    private sealed class ValidationFieldError
    {
        public string Field { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
