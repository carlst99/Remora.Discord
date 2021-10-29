//
//  DiscordRestWebhookAPI.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using OneOf;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Objects;
using Remora.Discord.Core;
using Remora.Discord.Rest.Extensions;
using Remora.Discord.Rest.Utility;
using Remora.Results;

namespace Remora.Discord.Rest.API
{
    /// <inheritdoc cref="Remora.Discord.API.Abstractions.Rest.IDiscordRestWebhookAPI" />
    [PublicAPI]
    public class DiscordRestWebhookAPI : AbstractDiscordRestAPI, IDiscordRestWebhookAPI
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordRestWebhookAPI"/> class.
        /// </summary>
        /// <param name="discordHttpClient">The Discord HTTP client.</param>
        /// <param name="jsonOptions">The json options.</param>
        public DiscordRestWebhookAPI(DiscordHttpClient discordHttpClient, IOptions<JsonSerializerOptions> jsonOptions)
            : base(discordHttpClient, jsonOptions)
        {
        }

        /// <inheritdoc />
        public virtual async Task<Result<IWebhook>> CreateWebhookAsync
        (
            Snowflake channelID,
            string name,
            Optional<Stream?> avatar,
            CancellationToken ct = default
        )
        {
            if (name.Length is < 1 or > 80)
            {
                return new ArgumentOutOfRangeError(nameof(name), "Names must be between 1 and 80 characters");
            }

            if (name.Equals("clyde", StringComparison.InvariantCultureIgnoreCase))
            {
                return new NotSupportedError("Names cannot be \"clyde\".");
            }

            var packAvatar = await ImagePacker.PackImageAsync(avatar, ct);
            if (!packAvatar.IsSuccess)
            {
                return Result<IWebhook>.FromError(packAvatar);
            }

            var avatarData = packAvatar.Entity;

            return await this.DiscordHttpClient.PostAsync<IWebhook>
            (
                $"channels/{channelID}/webhooks",
                b => b.WithJson
                (
                    json =>
                    {
                        json.WriteString("name", name);
                        json.Write("avatar", avatarData, this.JsonOptions);
                    }
                ),
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IReadOnlyList<IWebhook>>> GetChannelWebhooksAsync
        (
            Snowflake channelID,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.GetAsync<IReadOnlyList<IWebhook>>
            (
                $"channels/{channelID}/webhooks",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IReadOnlyList<IWebhook>>> GetGuildWebhooksAsync
        (
            Snowflake guildID,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.GetAsync<IReadOnlyList<IWebhook>>
            (
                $"guilds/{guildID}/webhooks",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IWebhook>> GetWebhookAsync
        (
            Snowflake webhookID,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.GetAsync<IWebhook>
            (
                $"webhooks/{webhookID}",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IWebhook>> GetWebhookWithTokenAsync
        (
            Snowflake webhookID,
            string token,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.GetAsync<IWebhook>
            (
                $"webhooks/{webhookID}/{token}",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual async Task<Result<IWebhook>> ModifyWebhookAsync
        (
            Snowflake webhookID,
            Optional<string> name = default,
            Optional<Stream?> avatar = default,
            Optional<Snowflake> channelID = default,
            CancellationToken ct = default
        )
        {
            var packAvatar = await ImagePacker.PackImageAsync(avatar, ct);
            if (!packAvatar.IsSuccess)
            {
                return Result<IWebhook>.FromError(packAvatar);
            }

            var avatarData = packAvatar.Entity;

            return await this.DiscordHttpClient.PatchAsync<IWebhook>
            (
                $"webhooks/{webhookID}",
                b => b.WithJson
                (
                    json =>
                    {
                        json.Write("name", name, this.JsonOptions);
                        json.Write("avatar", avatarData, this.JsonOptions);
                        json.Write("channel_id", channelID, this.JsonOptions);
                    }
                ),
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual async Task<Result<IWebhook>> ModifyWebhookWithTokenAsync
        (
            Snowflake webhookID,
            string token,
            Optional<string> name = default,
            Optional<Stream?> avatar = default,
            CancellationToken ct = default
        )
        {
            var packAvatar = await ImagePacker.PackImageAsync(avatar, ct);
            if (!packAvatar.IsSuccess)
            {
                return Result<IWebhook>.FromError(packAvatar);
            }

            var avatarData = packAvatar.Entity;

            return await this.DiscordHttpClient.PatchAsync<IWebhook>
            (
                $"webhooks/{webhookID}/{token}",
                b => b.WithJson
                (
                    json =>
                    {
                        json.Write("name", name, this.JsonOptions);
                        json.Write("avatar", avatarData, this.JsonOptions);
                    }
                ),
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result> DeleteWebhookAsync(Snowflake webhookID, CancellationToken ct = default)
        {
            return this.DiscordHttpClient.DeleteAsync
            (
                $"webhooks/{webhookID}",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result> DeleteWebhookWithTokenAsync
        (
            Snowflake webhookID,
            string token,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.DeleteAsync
            (
                $"webhooks/{webhookID}/{token}",
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IMessage?>> ExecuteWebhookAsync
        (
            Snowflake webhookID,
            string token,
            Optional<bool> shouldWait = default,
            Optional<string> content = default,
            Optional<string> username = default,
            Optional<string> avatarUrl = default,
            Optional<bool> isTTS = default,
            Optional<IReadOnlyList<IEmbed>> embeds = default,
            Optional<IAllowedMentions> allowedMentions = default,
            Optional<Snowflake> threadID = default,
            Optional<IReadOnlyList<IMessageComponent>> components = default,
            Optional<IReadOnlyList<OneOf<FileData, IPartialAttachment>>> attachments = default,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.PostAsync<IMessage?>
            (
                $"webhooks/{webhookID}/{token}",
                b =>
                {
                    if (shouldWait.HasValue)
                    {
                        b.AddQueryParameter("wait", shouldWait.Value.ToString());
                    }

                    Optional<IReadOnlyList<IPartialAttachment>> attachmentList = default;
                    if (attachments.HasValue)
                    {
                        // build attachment list
                        attachmentList = attachments.Value.Select
                        (
                            (f, i) => f.Match
                            (
                                data => new PartialAttachment(new Snowflake((ulong)i), data.Name, data.Description),
                                attachment => attachment
                            )
                        ).ToList();

                        for (var i = 0; i < attachments.Value.Count; i++)
                        {
                            if (!attachments.Value[i].IsT0)
                            {
                                continue;
                            }

                            var (name, stream, _) = attachments.Value[i].AsT0;
                            var contentName = $"files[{i}]";

                            b.AddContent(new StreamContent(stream), contentName, name);
                        }
                    }

                    b.WithJson
                    (
                        json =>
                        {
                            json.Write("content", content, this.JsonOptions);
                            json.Write("username", username, this.JsonOptions);
                            json.Write("avatar_url", avatarUrl, this.JsonOptions);
                            json.Write("tts", isTTS, this.JsonOptions);
                            json.Write("embeds", embeds, this.JsonOptions);
                            json.Write("allowed_mentions", allowedMentions, this.JsonOptions);
                            json.Write("thread_id", threadID, this.JsonOptions);
                            json.Write("components", components, this.JsonOptions);
                            json.Write("attachments", attachmentList, this.JsonOptions);
                        }
                    );
                },
                true,
                ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result<IMessage>> GetWebhookMessageAsync
        (
            Snowflake webhookID,
            string webhookToken,
            Snowflake messageID,
            Optional<Snowflake> threadID = default,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.GetAsync<IMessage>
            (
                $"webhooks/{webhookID}/{webhookToken}/messages/{messageID}",
                b =>
                {
                    if (threadID.HasValue)
                    {
                        b.AddQueryParameter("thread_id", threadID.Value.ToString());
                    }
                },
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual async Task<Result<IMessage>> EditWebhookMessageAsync
        (
            Snowflake webhookID,
            string token,
            Snowflake messageID,
            Optional<string?> content = default,
            Optional<IReadOnlyList<IEmbed>?> embeds = default,
            Optional<IAllowedMentions?> allowedMentions = default,
            Optional<IReadOnlyList<IMessageComponent>> components = default,
            Optional<IReadOnlyList<OneOf<FileData, IPartialAttachment>>> attachments = default,
            Optional<Snowflake> threadID = default,
            CancellationToken ct = default
        )
        {
            if (content.IsDefined(out var contentValue) && contentValue.Length > 2000)
            {
                return new NotSupportedError("Message content is too long (max 2000).");
            }

            if (embeds.IsDefined(out var embedsValue) && embedsValue.Count > 10)
            {
                return new NotSupportedError("Too many embeds (max 10).");
            }

            return await this.DiscordHttpClient.PatchAsync<IMessage>
            (
                $"webhooks/{webhookID}/{token}/messages/{messageID}",
                b =>
                {
                    Optional<IReadOnlyList<IPartialAttachment>> attachmentList = default;
                    if (attachments.HasValue)
                    {
                        // build attachment list
                        attachmentList = attachments.Value.Select
                        (
                            (f, i) => f.Match
                            (
                                data => new PartialAttachment(new Snowflake((ulong)i), data.Name, data.Description),
                                attachment => attachment
                            )
                        ).ToList();

                        for (var i = 0; i < attachments.Value.Count; i++)
                        {
                            if (!attachments.Value[i].IsT0)
                            {
                                continue;
                            }

                            var (name, stream, _) = attachments.Value[i].AsT0;
                            var contentName = $"files[{i}]";

                            b.AddContent(new StreamContent(stream), contentName, name);
                        }
                    }

                    if (threadID.HasValue)
                    {
                        b.AddQueryParameter("thread_id", threadID.Value.ToString());
                    }

                    b.WithJson
                    (
                        json =>
                        {
                            json.Write("content", content, this.JsonOptions);
                            json.Write("embeds", embeds, this.JsonOptions);
                            json.Write("allowed_mentions", allowedMentions, this.JsonOptions);
                            json.Write("components", components, this.JsonOptions);
                            json.Write("attachments", attachmentList, this.JsonOptions);
                        }
                    );
                },
                ct: ct
            );
        }

        /// <inheritdoc />
        public virtual Task<Result> DeleteWebhookMessageAsync
        (
            Snowflake webhookID,
            string token,
            Snowflake messageID,
            Optional<Snowflake> threadID = default,
            CancellationToken ct = default
        )
        {
            return this.DiscordHttpClient.DeleteAsync
            (
                $"webhooks/{webhookID}/{token}/messages/{messageID}",
                b =>
                {
                    if (threadID.HasValue)
                    {
                        b.AddQueryParameter("thread_id", threadID.Value.ToString());
                    }
                },
                ct
            );
        }
    }
}
