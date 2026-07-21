using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Executions;

internal sealed class AiExecutionAttemptConfiguration
    : EntityTypeConfigurationBase<AiExecutionAttempt, Guid>
{
    public override void Configure(EntityTypeBuilder<AiExecutionAttempt> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiExecutionAttempts");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ExecutionId).IsRequired();

        builder.Property(x => x.AttemptNumber).IsRequired();

        builder.Property(x => x.ConnectionId).IsRequired();

        builder.Property(x => x.ModelId).IsRequired();

        builder.Property(x => x.ProviderType).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.ExternalModelId).HasMaxLength(300).IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.StartedAtUtc).IsRequired();

        builder.Property(x => x.CompletedAtUtc).IsRequired(false);

        builder.Property(x => x.InputTokens).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.OutputTokens).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.EstimatedCost).HasPrecision(18, 8).HasDefaultValue(0m).IsRequired();

        builder.Property(x => x.DurationMilliseconds).HasDefaultValue(0L).IsRequired();

        builder.Property(x => x.FinishReason).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.ErrorCode).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.ErrorMessage).HasColumnType("text").IsRequired(false);

        builder
            .HasOne<AiConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<AiModel>()
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ExecutionId);

        builder.HasIndex(x => x.ConnectionId);

        builder.HasIndex(x => x.ModelId);

        builder.HasIndex(x => x.ProviderType);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.StartedAtUtc);

        builder
            .HasIndex(x => new { x.ExecutionId, x.AttemptNumber })
            .HasDatabaseName("ux_ai_execution_attempts_execution_number")
            .IsUnique();
    }
}
