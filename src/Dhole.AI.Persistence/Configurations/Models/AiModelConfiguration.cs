using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Models;

internal sealed class AiModelConfiguration : EntityTypeConfigurationBase<AiModel, Guid>
{
    public override void Configure(EntityTypeBuilder<AiModel> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiModels");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ConnectionId).IsRequired();

        builder.Property(x => x.ExternalModelId).HasMaxLength(300).IsRequired();

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();

        /*
         * Se guarda como entero porque AiModelCapability
         * es un enum de flags y debe permitir operaciones bitwise.
         */
        builder.Property(x => x.Capabilities).HasConversion<int>().IsRequired();

        builder.Property(x => x.ContextWindow).IsRequired(false);

        builder.Property(x => x.MaximumOutputTokens).IsRequired(false);

        builder.Property(x => x.InputCostPerMillionTokens).HasPrecision(18, 8).IsRequired(false);

        builder.Property(x => x.OutputCostPerMillionTokens).HasPrecision(18, 8).IsRequired(false);

        builder.Property(x => x.IsLocal).HasDefaultValue(false).IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.LastAvailabilityCheckAtUtc).IsRequired(false);

        builder.Property(x => x.LastAvailabilityError).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        builder
            .HasOne<AiConnection>()
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ConnectionId);

        builder.HasIndex(x => x.ExternalModelId);

        builder.HasIndex(x => x.Name);

        builder.HasIndex(x => x.Capabilities);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.IsLocal);

        builder.HasIndex(x => x.IsActive);

        builder
            .HasIndex(x => new { x.ConnectionId, x.ExternalModelId })
            .HasDatabaseName("ux_ai_models_connection_external_model")
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
