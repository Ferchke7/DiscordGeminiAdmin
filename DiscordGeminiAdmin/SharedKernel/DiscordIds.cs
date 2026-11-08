namespace DiscordGeminiAdmin.SharedKernel;

/// <summary>Идентификатор сервера (гильдии) Discord. Snowflake, не нулевой.</summary>
[Vogen.ValueObject<ulong>]
public readonly partial struct GuildId
{
    private static Vogen.Validation Validate(ulong value) =>
        value == 0 ? Vogen.Validation.Invalid("GuildId не может быть 0") : Vogen.Validation.Ok;
}

/// <summary>Идентификатор участника (пользователя) Discord. Snowflake, не нулевой.</summary>
[Vogen.ValueObject<ulong>]
public readonly partial struct MemberId
{
    private static Vogen.Validation Validate(ulong value) =>
        value == 0 ? Vogen.Validation.Invalid("MemberId не может быть 0") : Vogen.Validation.Ok;
}

/// <summary>Идентификатор канала Discord. Snowflake, не нулевой.</summary>
[Vogen.ValueObject<ulong>]
public readonly partial struct ChannelId
{
    private static Vogen.Validation Validate(ulong value) =>
        value == 0 ? Vogen.Validation.Invalid("ChannelId не может быть 0") : Vogen.Validation.Ok;
}

/// <summary>Идентификатор сообщения Discord. Snowflake, не нулевой.</summary>
[Vogen.ValueObject<ulong>]
public readonly partial struct MessageId
{
    private static Vogen.Validation Validate(ulong value) =>
        value == 0 ? Vogen.Validation.Invalid("MessageId не может быть 0") : Vogen.Validation.Ok;
}
