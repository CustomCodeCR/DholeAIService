using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Profiles.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Profiles;

internal sealed class AiProfileModelConfiguration
    : EntityTypeConfigurationBase<AiProfileModel, Guid>
{
    public override void Configure(EntityTypeBuilder<AiProfileModel> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiProfileModels");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ProfileId).IsRequired();

        builder.Property(x => x.ModelId).IsRequired();

        builder.Property(x => x.Priority).IsRequired();

        builder.Property(x => x.IsFallback).HasDefaultValue(false).IsRequired();

        builder
            .HasOne<AiModel>()
            .WithMany()
            .HasForeignKey(x => x.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ProfileId);

        builder.HasIndex(x => x.ModelId);

        builder.HasIndex(x => x.Priority);

        builder.HasIndex(x => x.IsFallback);

        builder
            .HasIndex(x => new { x.ProfileId, x.ModelId })
            .HasDatabaseName("ux_ai_profile_models_profile_model")
            .IsUnique();

        builder
            .HasIndex(x => new { x.ProfileId, x.Priority })
            .HasDatabaseName("ux_ai_profile_models_profile_priority")
            .IsUnique();
    }
}
