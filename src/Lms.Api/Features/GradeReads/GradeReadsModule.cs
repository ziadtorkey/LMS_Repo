using Lms.Api.Infrastructure;

namespace Lms.Api.Features.GradeReads;

/// <summary>Published-only grade reads for students and guardians, the staff preview and grade reports (M5 Task 9). Services come from the infrastructure.</summary>
public sealed class GradeReadsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        StudentGradeEndpoints.Map(app);
        StudentViewPreviewEndpoint.Map(app);
        GradeReportEndpoints.Map(app);
    }
}
