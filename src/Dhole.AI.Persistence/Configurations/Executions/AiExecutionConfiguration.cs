using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Executions;

internal sealed class AiExecutionConfiguration : EntityTypeConfigurationBase<AiExecution, Guid>
{
    public override void Configure(EntityTypeBuilder<AiExecution> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiExecutions");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ProfileId).IsRequired();

        builder.Property(x => x.ProfileKey).HasMaxLength(150).IsRequired();

        builder.Property(x => x.PromptTemplateId).IsRequired(false);

        builder
            .Property(x => x.ExecutionType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.CorrelationId).HasMaxLength(150).IsRequired(false);

        builder.Property(x => x.RequestHash).HasMaxLength(256).IsRequired(false);

        builder.Property(x => x.OutputReference).HasMaxLength(1_000).IsRequired(false);

        builder.Property(x => x.SelectedConnectionId).IsRequired(false);

        builder.Property(x => x.SelectedModelId).IsRequired(false);

        builder.Property(x => x.InputTokens).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.OutputTokens).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.EstimatedCost).HasPrecision(18, 8).HasDefaultValue(0m).IsRequired();

        builder.Property(x => x.DurationMilliseconds).HasDefaultValue(0L).IsRequired();

        builder.Property(x => x.FinishReason).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.ErrorCode).HasMaxLength(250).IsRequired(false);

        builder.Property(x => x.ErrorMessage).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.StartedAtUtc).IsRequired(false);

        builder.Property(x => x.CompletedAtUtc).IsRequired(false);

        builder.Property(x => x.CancelledAtUtc).IsRequired(false);

        builder.Property(x => x.CancellationReason).HasColumnType("text").IsRequired(false);

        builder
            .HasOne<AiProfile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<AiPromptTemplate>()
            .WithMany()
            .HasForeignKey(x => x.PromptTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<AiConnection>()
            .WithMany()
            .HasForeignKey(x => x.SelectedConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<AiModel>()
            .WithMany()
            .HasForeignKey(x => x.SelectedModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(x => x.Attempts)
            .WithOne()
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.ProfileId);

        builder.HasIndex(x => x.ProfileKey);

        builder.HasIndex(x => x.PromptTemplateId);

        builder.HasIndex(x => x.ExecutionType);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.CorrelationId);

        builder.HasIndex(x => x.RequestHash);

        builder.HasIndex(x => x.SelectedConnectionId);

        builder.HasIndex(x => x.SelectedModelId);

        builder.HasIndex(x => x.StartedAtUtc);

        builder.HasIndex(x => x.CompletedAtUtc);

        builder
            .HasIndex(x => new
            {
                x.ProfileKey,
                x.Status,
                x.StartedAtUtc,
            })
            .HasDatabaseName("ix_ai_executions_profile_status_started");
    }
}
