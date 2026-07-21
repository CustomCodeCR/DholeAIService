using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.AI.Persistence.Configurations.PromptTemplates;

internal sealed class AiPromptTemplateConfiguration
    : EntityTypeConfigurationBase<AiPromptTemplate, Guid>
{
    public override void Configure(EntityTypeBuilder<AiPromptTemplate> builder)
    {
        base.Configure(builder);

        builder.ToTable("AiPromptTemplates");

        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Key).HasMaxLength(150).IsRequired();

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();

        builder.Property(x => x.Description).HasMaxLength(1_000).IsRequired(false);

        builder.Property(x => x.SystemPrompt).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.UserPromptTemplate).HasColumnType("text").IsRequired(false);

        builder.Property(x => x.VariablesJson).HasColumnType("jsonb").IsRequired(false);

        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => x.Key);

        builder.HasIndex(x => x.Name);

        builder.HasIndex(x => x.IsActive);

        builder
            .HasIndex(x => x.Key)
            .HasDatabaseName("ux_ai_prompt_templates_key")
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
