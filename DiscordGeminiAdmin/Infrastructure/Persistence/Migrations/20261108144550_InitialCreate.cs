using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DiscordGeminiAdmin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "buffered_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    channel_id = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    discord_message_id = table.Column<string>(type: "text", nullable: false),
                    author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_buffered_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guild_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    theme = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    mod_channel_id = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    moderation_policy_dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    moderation_policy_max_auto_bans_per_hour = table.Column<int>(type: "integer", nullable: false),
                    moderation_policy_min_confidence = table.Column<double>(type: "double precision", nullable: false),
                    moderation_policy_min_evidence_messages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hourly_digests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hourly_digests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(768)", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_chunks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "moderation_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    member_id = table.Column<string>(type: "text", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    ban_source = table.Column<int>(type: "integer", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    assessment_confidence = table.Column<double>(type: "double precision", nullable: false),
                    assessment_evidence_count = table.Column<int>(type: "integer", nullable: false),
                    assessment_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    assessment_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_cases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "structure_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guild_id = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_structure_proposals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_buffered_messages_guild_id_created_at",
                table: "buffered_messages",
                columns: new[] { "guild_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_guild_profiles_guild_id",
                table: "guild_profiles",
                column: "guild_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_embedding",
                table: "knowledge_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_guild_id",
                table: "knowledge_chunks",
                column: "guild_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_guild_id_rule_code",
                table: "knowledge_documents",
                columns: new[] { "guild_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_moderation_cases_guild_id_resolved_at",
                table: "moderation_cases",
                columns: new[] { "guild_id", "resolved_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "buffered_messages");

            migrationBuilder.DropTable(
                name: "guild_profiles");

            migrationBuilder.DropTable(
                name: "hourly_digests");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_documents");

            migrationBuilder.DropTable(
                name: "moderation_cases");

            migrationBuilder.DropTable(
                name: "structure_proposals");
        }
    }
}
