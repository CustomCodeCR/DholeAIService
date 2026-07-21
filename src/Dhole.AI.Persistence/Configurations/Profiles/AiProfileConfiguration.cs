using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.Profiles;

internal sealed class AiProfileConfiguration : EntityTypeConfigurationBase<AiProfile, Guid>
{
    public override void Configure(EntityTypeBuilder<AiProfile> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiProfiles");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Key).HasMaxLength(150).IsRequired();

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();

        builder.Property(x => x.Description).HasMaxLength(1_000).IsRequired(false);

        builder.Property(x => x.PromptTemplateId).IsRequired(false);

        builder.Property(x => x.RoutingMode).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder
            .Property(x => x.ResponseFormat)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Temperature).HasPrecision(5, 2).IsRequired();

        builder.Property(x => x.MaximumOutputTokens).IsRequired();

        builder.Property(x => x.TimeoutSeconds).IsRequired();

        builder.Property(x => x.JsonSchema).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.IsActive).HasDefaultValue(false).IsRequired();

        builder
            .HasOne<AiPromptTemplate>()
            .WithMany()
            .HasForeignKey(x => x.PromptTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasMany(x => x.Models)
            .WithOne()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Models).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.Key);

        builder.HasIndex(x => x.Name);

        builder.HasIndex(x => x.PromptTemplateId);

        builder.HasIndex(x => x.RoutingMode);

        builder.HasIndex(x => x.ResponseFormat);

        builder.HasIndex(x => x.IsActive);

        builder
            .HasIndex(x => x.Key)
            .HasDatabaseName("ux_ai_profiles_key")
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
