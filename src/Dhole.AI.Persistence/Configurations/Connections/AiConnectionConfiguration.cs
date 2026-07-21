using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Connections.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Connections;

internal sealed class AiConnectionConfiguration : EntityTypeConfigurationBase<AiConnection, Guid>
{
    public override void Configure(EntityTypeBuilder<AiConnection> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiConnections");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();

        builder.Property(x => x.ProviderType).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.BaseUrl).HasMaxLength(1_000).IsRequired();

        builder.Property(x => x.SecretReference).HasMaxLength(500).IsRequired(false);

        builder.Property(x => x.TimeoutSeconds).IsRequired();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.Property(x => x.LastHealthCheckAtUtc).IsRequired(false);

        builder.Property(x => x.LastHealthError).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => x.Name);

        builder.HasIndex(x => x.ProviderType);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.IsActive);

        builder.HasIndex(x => x.LastHealthCheckAtUtc);

        builder
            .HasIndex(x => x.Name)
            .HasDatabaseName("ux_ai_connections_name")
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
