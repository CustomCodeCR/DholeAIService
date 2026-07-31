using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.EmailAnalysis;

internal sealed class AiEmailAnalysisJobConfiguration
    : EntityTypeConfigurationBase<AiEmailAnalysisJob, Guid>
{
    public override void Configure(EntityTypeBuilder<AiEmailAnalysisJob> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiEmailAnalysisJobs");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ExternalRequestId).IsRequired();
        builder.Property(x => x.EmailExtractionJobId).IsRequired();
        builder.Property(x => x.EmailMessageId).IsRequired();
        builder.Property(x => x.EmailAttachmentId);
        builder.Property(x => x.PayloadUrl).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttemptCount).HasDefaultValue(3).IsRequired();
        builder.Property(x => x.NextAttemptAtUtc);
        builder.Property(x => x.LeaseOwner).HasMaxLength(250);
        builder.Property(x => x.LeaseExpiresAtUtc);
        builder.Property(x => x.LastHeartbeatAtUtc);
        builder.Property(x => x.AiExecutionId);
        builder.Property(x => x.ResultJson).HasColumnType("jsonb");
        builder.Property(x => x.ErrorCode).HasMaxLength(250);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.StartedAtUtc);
        builder.Property(x => x.CompletedAtUtc);
        builder.Property(x => x.Version).HasDefaultValue(1).IsConcurrencyToken();

        builder.HasIndex(x => x.ExternalRequestId).IsUnique();
        builder.HasIndex(x => x.EmailExtractionJobId);
        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.RequestHash);
        builder.HasIndex(x => x.AiExecutionId);
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.NextAttemptAtUtc,
                x.CreatedAtUtc,
            })
            .HasDatabaseName("ix_ai_email_jobs_queue");
        builder
            .HasIndex(x => new
            {
                x.Status,
                x.LeaseExpiresAtUtc,
            })
            .HasDatabaseName("ix_ai_email_jobs_lease");
    }
}
