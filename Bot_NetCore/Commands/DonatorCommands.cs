﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bot_NetCore.Entities;
using Bot_NetCore.Misc;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;

namespace Bot_NetCore.Commands
{
    [Group("donator")]
    [Aliases("d")]
    [Description("Команды доната.")]
    [RequireGuild]
    public class DonatorCommands : BaseCommandModule
    {
        [Command("setprice")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task SetPrice(CommandContext ctx, string name, int newPrice)
        {
            if (!DonatorPriceListSQL.Prices.ContainsKey(DateTime.Today))
            {
                var latestPrices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(DateTime.Now)];
                DonatorPriceListSQL.Prices[DateTime.Today] = new DateServices(DateTime.Today, latestPrices.ColorPrice,
                    latestPrices.WantedPrice, latestPrices.RolePrice, latestPrices.FriendsPrice);
            }

            switch (name)
            {
                case "color":
                    DonatorPriceListSQL.Prices[DateTime.Today].ColorPrice = newPrice;
                    break;
                case "wanted":
                    DonatorPriceListSQL.Prices[DateTime.Today].WantedPrice = newPrice;
                    break;
                case "role":
                    DonatorPriceListSQL.Prices[DateTime.Today].RolePrice = newPrice;
                    break;
                case "friends":
                    DonatorPriceListSQL.Prices[DateTime.Today].FriendsPrice = newPrice;
                    break;
                default:
                    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Неправильно указано имя услуги!");
                    return;
            }

            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменена цена услуги!");

            DonatorPriceListSQL.SavePrices();
        }

        [Command("getprices")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task GetPrices(CommandContext ctx)
        {
            var lastDate = DonatorPriceListSQL.GetLastDate(DateTime.Now);
            var prices = DonatorPriceListSQL.Prices[lastDate];

            var embed = new DiscordEmbedBuilder();
            embed.Color = DiscordColor.Goldenrod;
            embed.Title = "Текущие цены на донат";
            embed.AddField("Color", prices.ColorPrice.ToString(), true);
            embed.AddField("Wanted", prices.WantedPrice.ToString(), true);
            embed.AddField("Role", prices.RolePrice.ToString(), true);
            embed.AddField("Friends", prices.FriendsPrice.ToString(), true);

            embed.WithTimestamp(lastDate);

            await ctx.RespondAsync(embed: embed.Build());
        }

        [Command("add")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task Add(CommandContext ctx, DiscordMember member, int balance)
        {
            await ctx.TriggerTypingAsync();

            ctx.Client.Logger.LogDebug(BotLoggerEvents.Commands, $"Adding new donator {member} with {balance} balance");

            var donator = DonatorSQL.GetById(member.Id);
            if (donator == null)
                donator = DonatorSQL.Create(member.Id, DateTime.Now, balance: balance);
            else
                donator.Balance += balance;

            if (donator.PrivateRole != 0 && !CheckPermisson(donator, DonatorPermissions.Role))
            {
                try
                {
                    ctx.Client.Logger.LogDebug(BotLoggerEvents.Commands, $"Deleting donator role {donator.PrivateRole}");
                    await ctx.Guild.GetRole(donator.PrivateRole).DeleteAsync();
                }
                catch (NotFoundException) { }
                catch (NullReferenceException) { }

                donator.PrivateRole = 0;
            }

            var message = $"Спасибо за поддержку нашего сообщества! **Ваш баланс: {donator.Balance} ₽.\n" +
                          $"Доступные функции:**\n";

            if (CheckPermisson(donator, DonatorPermissions.Color) && !CheckPermisson(donator, DonatorPermissions.Role))
            {
                message += $"• `{Bot.BotSettings.Prefix}d color цвет (из списка)` — изменяет цвет вашего ника.\n";
                message += $"• `{Bot.BotSettings.Prefix}d colors` — выводит список доступных цветов.\n";
            }

            if (CheckPermisson(donator, DonatorPermissions.Role))
            {
                message += $"• `{Bot.BotSettings.Prefix}d color hex-код цвета` — изменяет цвет вашего ника.\n";
                message += $"• `{Bot.BotSettings.Prefix}d rename` — изменяет название вашей роли донатера.\n";
            }

            if (CheckPermisson(donator, DonatorPermissions.Wanted))
                message += $"• `{Bot.BotSettings.Prefix}d roleadd` — выдаёт вам роль `💣☠️WANTED☠️💣`.\n" +
                           $"• `{Bot.BotSettings.Prefix}d rolerm` — снимает с вас роль `💣☠️WANTED☠️💣`.\n";

            if (CheckPermisson(donator, DonatorPermissions.Friends))
                message += $"• `{Bot.BotSettings.Prefix}d friend` — добавляет другу ваш цвет (до 5 друзей).\n" +
                           $"• `{Bot.BotSettings.Prefix}d unfriend` — убирает у друга ваш цвет.";

            try
            {
                await member.SendMessageAsync(message);
            }
            catch (UnauthorizedException) { }

            if (donator.Balance == balance)
                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно добавлен донатер! Баланс: **{donator.Balance}**"); //Новый донатер
            else
                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменен баланс: **{donator.Balance}**!"); //Уже существующий донатер
        }

        [Command("addsub")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task AddSubscriber(CommandContext ctx, DiscordMember member, [Description("Количество дней")] int time)
        {
            var donator = DonatorSQL.GetById(member.Id);
            if (donator == null)
                donator = DonatorSQL.Create(member.Id, DateTime.Now, subEnd: DateTime.Now.AddDays(time));
            else if (donator.IsSubscriber())
                donator.subEnd = donator.subEnd.AddDays(time);
            else
                donator.subEnd = DateTime.Now.AddDays(time);

            await member.SendMessageAsync(
                $"Спасибо за поддержку нашего сообщества! Ваша подписка истекает **{donator.subEnd:HH:mm:ss dd.MM.yyyy}**.\n" +
                $"**Доступные возможности:**\n" +
                $"• `{Bot.BotSettings.Prefix}d color hex-код цвета` — изменяет цвет вашего ника.\n" +
                $"• `{Bot.BotSettings.Prefix}d rename` — изменяет название вашей роли донатера.\n" +
                $"• `{Bot.BotSettings.Prefix}d roleadd` — выдаёт вам роль `💣☠️WANTED☠️💣`.\n" +
                $"• `{Bot.BotSettings.Prefix}d rolerm` — снимает с вас роль `💣☠️WANTED☠️💣`.\n" +
                $"• `{Bot.BotSettings.Prefix}d friend` — добавляет другу ваш цвет (до 5 друзей).\n" +
                $"• `{Bot.BotSettings.Prefix}d unfriend` — убирает у друга ваш цвет.");

            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно добавлен подписчик!");
        }

        [Command("remove")]
        [Aliases("rm")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task Remove(CommandContext ctx, DiscordMember member)
        {
            await ctx.TriggerTypingAsync();

            var donator = DonatorSQL.GetById(member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Пользователь не является донатером!");
                return;
            }

            if (donator.PrivateRole != 0)
            {
                try
                {
                    await ctx.Guild.GetRole(donator.PrivateRole).DeleteAsync();
                }
                catch (NotFoundException) { }
                catch (NullReferenceException) { }
            }

            DonatorSQL.RemoveDonator(donator.UserId);

            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удалён донатер!");
        }

        [Command("color")]
        [Description("Устанавливает донатерский цвет. Формат: #000000 для владельцев приватных ролей, либо название цвета.")]
        public async Task Color(CommandContext ctx, [RemainingText] string color)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (!CheckPermisson(donator, DonatorPermissions.Color))
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            }
            else if (donator.Balance >= prices.ColorPrice && donator.Balance < prices.RolePrice)
            {
                var avaliableRoles = GetColorRolesIds(ctx.Guild);
                if (avaliableRoles == null)
                {
                    await ctx.RespondAsync(
                        $"{Bot.BotSettings.ErrorEmoji} Не заданы цветные роли! Обратитесь к администратору для решения проблемы.");
                    return;
                }

                foreach (var role in avaliableRoles)
                {
                    if (role.Name.ToLower() == color.ToLower())
                    {
                        var isSameRole = false;
                        foreach (var memberRole in ctx.Member.Roles)
                            if (avaliableRoles.Contains(memberRole))
                            {
                                if (role.Id == memberRole.Id)
                                    isSameRole = true;

                                await ctx.Member.RevokeRoleAsync(memberRole);
                                break;
                            }

                        if (!isSameRole)
                        {
                            await ctx.Member.GrantRoleAsync(role);
                            await role.ModifyPositionAsync(ctx.Guild.GetRole(Bot.BotSettings.ColorSpacerRole).Position - 1); // костыльное решение невозможности нормально перенести роли
                            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменён цвет!");
                        }
                        else
                        {
                            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удалён цвет!");
                        }
                        return;
                    }
                }

                await ctx.RespondAsync(
                    $"{Bot.BotSettings.ErrorEmoji} Неправильно задано имя цвета! Используйте `!d colors`, чтобы получить список доступых цветов. ");
            }
            else if (donator.Balance >= prices.RolePrice)
            {
                var role = await GetDonatorRoleOrCreate(ctx.Guild, ctx.Member, donator);
                try
                {
                    await role.ModifyAsync(x => x.Color = new DiscordColor(color));
                    await role.ModifyPositionAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorSpacerRole).Position - 1);
                    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменён цвет!");
                }
                catch (ArgumentException)
                {
                    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Неправильно заданый цвет! Формат: #000000 для владельцев приватных ролей.");
                }
            }


            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    var role = ctx.Guild.GetRole(Subscriber.Subscribers[ctx.Member.Id].PrivateRole);
            //    await role.ModifyAsync(x => x.Color = new DiscordColor(color));
            //    await role.ModifyPositionAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorSpacerRole).Position - 1);

            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменён цвет!");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id)) // для обычных донатеров
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];
            //    var donator = Donator.Donators[ctx.Member.Id];

            //    if (donator.Balance < prices.ColorPrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //    }
            //    else if (donator.Balance >= prices.ColorPrice && donator.Balance < prices.RolePrice)
            //    {
            //        var avaliableRoles = GetColorRolesIds(ctx.Guild);
            //        if (avaliableRoles == null)
            //        {
            //            await ctx.RespondAsync(
            //                $"{Bot.BotSettings.ErrorEmoji} Не заданы цветные роли! Обратитесь к администратору для решения проблемы.");
            //            return;
            //        }

            //        foreach (var role in avaliableRoles)
            //        {
            //            if (role.Name.ToLower() == color.ToLower())
            //            {
            //                foreach (var memberRole in ctx.Member.Roles)
            //                    if (avaliableRoles.Contains(memberRole))
            //                    {
            //                        await ctx.Member.RevokeRoleAsync(memberRole);
            //                        break;
            //                    }

            //                await ctx.Member.GrantRoleAsync(role);
            //                await role.ModifyPositionAsync(ctx.Guild.GetRole(Bot.BotSettings.ColorSpacerRole).Position - 1); // костыльное решение невозможности нормально перенести роли
            //                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменён цвет!");
            //                return;
            //            }
            //        }

            //        await ctx.RespondAsync(
            //            $"{Bot.BotSettings.ErrorEmoji} Неправильно задано имя цвета! Используйте `!d colors`, чтобы получить список доступых цветов. ");
            //    }
            //    else if (donator.Balance >= prices.RolePrice)
            //    {
            //        var role = ctx.Guild.GetRole(donator.PrivateRole);
            //        try
            //        {
            //            await role.ModifyAsync(x => x.Color = new DiscordColor(color));
            //            await role.ModifyPositionAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorSpacerRole).Position - 1);
            //            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно изменён цвет!");
            //        }
            //        catch (ArgumentException)
            //        {
            //            await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Неправильно заданый цвет! Формат: #000000 для владельцев приватных ролей.");
            //        }
            //    }
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("colors")]
        [Description("Выводит список доступных цветов.")]
        public async Task Colors(CommandContext ctx)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            if (!File.Exists("generated/colors.jpeg"))
            {
                await ctx.RespondAsync(
                    $"{Bot.BotSettings.ErrorEmoji} Отсутствует файл colors.jpeg, обратитесь к администратору!");
                return;
            }

            var message = new DiscordMessageBuilder()
                .WithContent("Используйте `!d color название цвета`, чтобы получить цвет.\n" +
                "Для удаления цвета, повторно введите `!d color название цвета`.")
                .WithFile("generated/colors.jpeg");

            await ctx.RespondAsync(message);
        }

        [Command("colorrm")]
        [Description("Удаляет донатерский цвет/роль.")]
        public async Task ColorRm(CommandContext ctx)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.ColorPrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            }
            else if (donator.Balance >= prices.ColorPrice && donator.Balance < prices.RolePrice)
            {
                var avaliableRoles = GetColorRolesIds(ctx.Guild);
                if (avaliableRoles == null)
                {
                    await ctx.RespondAsync(
                        $"{Bot.BotSettings.ErrorEmoji} Не заданы цветные роли! Обратитесь к администратору для решения проблемы.");
                    return;
                }

                foreach (var memberRole in ctx.Member.Roles)
                    if (avaliableRoles.Contains(memberRole))
                    {
                        await ctx.Member.RevokeRoleAsync(memberRole);
                        break;
                    }

                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет!");
            }
            else if (donator.Balance >= prices.RolePrice)
            {
                if (donator.PrivateRole == 0)
                {
                    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} У вас нет приватной роли.");
                }
                else
                {
                    var role = await GetDonatorRoleOrCreate(ctx.Guild, ctx.Member, donator);
                    foreach (var memberRole in ctx.Member.Roles)
                        if (role == memberRole)
                        {
                            await ctx.Member.RevokeRoleAsync(role);
                            break;
                        }

                    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удалена приватная роль!");
                }
            }
        }

        [Command("generatecolors")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task GenerateColors(CommandContext ctx)
        {
            if (File.Exists("generated/color_roles.txt"))
                using (var fs = new FileStream("generated/color_roles.txt", FileMode.Open))
                using (var sr = new StreamReader(fs))
                {
                    var role = await sr.ReadLineAsync();
                    while (role != null)
                    {
                        await ctx.Guild.GetRole(Convert.ToUInt64(role)).DeleteAsync();
                        role = await sr.ReadLineAsync();
                    }
                }

            var colors = new Dictionary<string, DiscordColor>
            {
                ["Dark red"] = DiscordColor.DarkRed,
                ["Red"] = DiscordColor.Red,
                ["Pink"] = new DiscordColor("FFC0CB"),
                ["Deep orange"] = new DiscordColor("FF8C00"),
                ["Orange"] = DiscordColor.Orange,
                ["Light orange"] = new DiscordColor("FFA161"),
                ["Dark yellow"] = new DiscordColor("B07D2B"),
                ["Yellow"] = DiscordColor.Yellow,
                ["Pale yellow"] = new DiscordColor("FFDB8B"),
                ["Dark green"] = DiscordColor.DarkGreen,
                ["Green"] = DiscordColor.Green,
                ["Light green"] = new DiscordColor("90EE90"),
                ["Dark cyan"] = new DiscordColor("3B83BD"),
                ["Cyan"] = DiscordColor.Cyan,
                ["Light cyan"] = new DiscordColor("87CEFA"),
                ["Dark blue"] = DiscordColor.DarkBlue,
                ["Blue"] = DiscordColor.Blue,
                ["Light blue"] = new DiscordColor("A6CAF0"),
                ["Dark purple"] = new DiscordColor("9400D3"),
                ["Purple"] = DiscordColor.Purple,
                ["Light purple"] = new DiscordColor("876C99"),
            };

            var roles = new List<ulong>();
            using (var fs = File.Create("generated/color_roles.txt"))
            using (var sw = new StreamWriter(fs))
                foreach (var color in colors)
                {
                    var role = await ctx.Guild.CreateRoleAsync(color.Key, color: color.Value);
                    roles.Add(role.Id);
                    await sw.WriteLineAsync(role.Id.ToString());
                }

            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно сгенерированы цветные роли!");
        }

        [Command("rename")]
        [Description("Измененяет название роли донатера.")]
        public async Task Rename(CommandContext ctx, [RemainingText] string newName)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.RolePrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
                return;
            }
            //Проверка названия на копирование админ ролей
            try
            {
                if (Bot.GetMultiplySettingsSeparated(Bot.BotSettings.AdminRoles)
                    .Any(x => ctx.Guild.GetRole(x).Name.Equals(newName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Недопустимое название роли **{newName}**");
                    return;
                }
            }
            catch (NullReferenceException ex)
            {
                //Не находит на сервере одну из админ ролей
                throw new NullReferenceException("Impossible to find one of admin roles. Check configuration", ex);
            }

            var role = await GetDonatorRoleOrCreate(ctx.Guild, ctx.Member, donator);
            await role.ModifyAsync(x => x.Name = newName);
            await ctx.RespondAsync(
                $"{Bot.BotSettings.OkEmoji} Успешно изменено название роли донатера на **{newName}**");

            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    try //Проверка названия на копирование админ ролей
            //    {
            //        if (Bot.GetMultiplySettingsSeparated(Bot.BotSettings.AdminRoles)
            //            .Any(x => ctx.Guild.GetRole(x).Name.Equals(newName, StringComparison.InvariantCultureIgnoreCase)))
            //        {
            //            await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Недопустимое название роли **{newName}**");
            //            return;
            //        }
            //    }
            //    catch (NullReferenceException ex)
            //    {
            //        Не находит на сервере одну из админ ролей
            //        throw new NullReferenceException("Impossible to find one of admin roles. Check configuration", ex);
            //    }

            //    var role = ctx.Guild.GetRole(Subscriber.Subscribers[ctx.Member.Id].PrivateRole);
            //    await role.ModifyAsync(x => x.Name = newName);
            //    await ctx.RespondAsync(
            //        $"{Bot.BotSettings.OkEmoji} Успешно изменено название роли донатера на **{newName}**");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id))
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];

            //    if (Donator.Donators[ctx.Member.Id].Balance < prices.RolePrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //        return;
            //    }
            //    Проверка названия на копирование админ ролей
            //    try
            //    {
            //        if (Bot.GetMultiplySettingsSeparated(Bot.BotSettings.AdminRoles)
            //            .Any(x => ctx.Guild.GetRole(x).Name.Equals(newName, StringComparison.InvariantCultureIgnoreCase)))
            //        {
            //            await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Недопустимое название роли **{newName}**");
            //            return;
            //        }
            //    }
            //    catch (NullReferenceException ex)
            //    {
            //        Не находит на сервере одну из админ ролей
            //        throw new NullReferenceException("Impossible to find one of admin roles. Check configuration", ex);
            //    }

            //    var role = ctx.Guild.GetRole(Donator.Donators[ctx.Member.Id].PrivateRole);
            //    await role.ModifyAsync(x => x.Name = newName);
            //    await ctx.RespondAsync(
            //        $"{Bot.BotSettings.OkEmoji} Успешно изменено название роли донатера на **{newName}**");
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("friend")]
        [Description("Добавляет вашему другу цвет донатера (ваш)")]
        public async Task Friend(CommandContext ctx, DiscordMember member)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.FriendsPrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
                return;
            }

            if (donator.GetFriends().Count >= donator.Balance / 100)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы можете добавить только 5 друзей!");
                return;
            }

            if (donator.GetFriends().Contains(member.Id))
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы уже добавили этого друга!");
                return;
            }

            donator.AddFriend(member.Id);
            var role = await GetDonatorRoleOrCreate(ctx.Guild, ctx.Member, donator);
            await member.GrantRoleAsync(role);

            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Вы успешно добавили вашему другу цвет!");


            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    if (Subscriber.Subscribers[ctx.Member.Id].Friends.Count == 5)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы можете добавить только 5 друзей!");
            //        return;
            //    }

            //    Subscriber.Subscribers[ctx.Member.Id].Friends.Add(member.Id);
            //    await member.GrantRoleAsync(ctx.Guild.GetRole(Subscriber.Subscribers[ctx.Member.Id].PrivateRole));
            //    Subscriber.Save(Bot.BotSettings.SubscriberXML);

            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Вы успешно добавили вашему другу цвет!");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id))
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];

            //    if (Donator.Donators[ctx.Member.Id].Balance < prices.FriendsPrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //        return;
            //    }

            //    if (Donator.Donators[ctx.Member.Id].Friends.Count == 5)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы можете добавить только 5 друзей!");
            //        return;
            //    }

            //    Donator.Donators[ctx.Member.Id].Friends.Add(member.Id);
            //    await member.GrantRoleAsync(ctx.Guild.GetRole(Donator.Donators[ctx.Member.Id].PrivateRole));
            //    Donator.Save(Bot.BotSettings.DonatorXML);

            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Вы успешно добавили вашему другу цвет!");
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("unfriend")]
        [Description("Убирает цвет у друга")]
        public async Task Unfriend(CommandContext ctx, DiscordMember member)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            var friendDonator = DonatorSQL.GetById(member.Id);

            if (friendDonator != null && friendDonator.PrivateRole != 0)
            {
                friendDonator.RemoveFriend(ctx.Member.Id);
                await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(friendDonator.PrivateRole));
                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет вашего друга!");
            }

            if (donator != null && donator.PrivateRole != 0)
            {
                donator.RemoveFriend(member.Id);
                await member.RevokeRoleAsync(ctx.Guild.GetRole(donator.PrivateRole));
                await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет у вашего друга!");
            }

            ////Удаление роли которую дали
            //if (Subscriber.Subscribers.ContainsKey(member.Id) &&
            //    Subscriber.Subscribers[member.Id].Friends.Contains(ctx.Member.Id))
            //{
            //    Subscriber.Subscribers[member.Id].Friends.Remove(ctx.Member.Id);
            //    await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(Subscriber.Subscribers[member.Id].PrivateRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет вашего друга!");
            //    Subscriber.Save(Bot.BotSettings.DonatorXML);
            //}
            //else if (Donator.Donators.ContainsKey(member.Id) &&
            //    Donator.Donators[member.Id].Friends.Contains(ctx.Member.Id))
            //{
            //    Donator.Donators[member.Id].Friends.Remove(ctx.Member.Id);
            //    await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(Donator.Donators[member.Id].PrivateRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет вашего друга!");
            //    Donator.Save(Bot.BotSettings.DonatorXML);
            //}

            ////Удаление своей роли
            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id) &&
            //    Subscriber.Subscribers[ctx.Member.Id].Friends.Contains(member.Id))
            //{
            //    Subscriber.Subscribers[ctx.Member.Id].Friends.Remove(member.Id);
            //    await member.RevokeRoleAsync(ctx.Guild.GetRole(Subscriber.Subscribers[ctx.Member.Id].PrivateRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет у вашего друга!");
            //    Subscriber.Save(Bot.BotSettings.SubscriberXML);
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id) &&
            //          Donator.Donators[ctx.Member.Id].Friends.Contains(member.Id))
            //{
            //    Donator.Donators[ctx.Member.Id].Friends.Remove(member.Id);
            //    await member.RevokeRoleAsync(ctx.Guild.GetRole(Donator.Donators[ctx.Member.Id].PrivateRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно удален цвет у вашего друга!");
            //    Donator.Save(Bot.BotSettings.DonatorXML);
            //}
        }

        [Command("friends")]
        [Description("Выводит список друзей")]
        public async Task Friends(CommandContext ctx)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.FriendsPrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
                return;
            }

            var i = 0;
            var friendsMsg = "";
            var friends = donator.GetFriends();
            foreach (var friend in friends)
            {
                DiscordUser discordMember = null;
                try
                {
                    discordMember = await ctx.Client.GetUserAsync(friend);
                }
                catch (DSharpPlus.Exceptions.NotFoundException)
                {
                    continue;
                }
                i++;
                friendsMsg += $"**{i}**. {discordMember.Username}#{discordMember.Discriminator} \n";
            }
            await ctx.RespondAsync($"**Друзья с твоей ролью ({friends.Count}/{donator.Balance / 100})**\n\n" +
                                   $"{friendsMsg}");

            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    var i = 0;
            //    var friendsMsg = "";
            //    foreach (var friend in Subscriber.Subscribers[ctx.Member.Id].Friends)
            //    {
            //        DiscordMember discordMember = null;
            //        try
            //        {
            //            discordMember = await ctx.Guild.GetMemberAsync(friend);
            //        }
            //        catch (DSharpPlus.Exceptions.NotFoundException)
            //        {
            //            continue;
            //        }
            //        i++;
            //        friendsMsg += $"**{i}**. {discordMember.DisplayName}#{discordMember.Discriminator} \n";
            //    }
            //    await ctx.RespondAsync("**Список друзей с вашей ролью**\n\n" +
            //                           $"{friendsMsg}");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id))
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];

            //    if (Donator.Donators[ctx.Member.Id].Balance < prices.FriendsPrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //        return;
            //    }

            //    var i = 0;
            //    var friendsMsg = "";
            //    foreach (var friend in Donator.Donators[ctx.Member.Id].Friends)
            //    {
            //        DiscordMember discordMember = null;
            //        try
            //        {
            //            discordMember = await ctx.Guild.GetMemberAsync(friend);
            //        }
            //        catch (DSharpPlus.Exceptions.NotFoundException)
            //        {
            //            continue;
            //        }
            //        i++;
            //        friendsMsg += $"**{i}**. {discordMember.DisplayName}#{discordMember.Discriminator} \n";
            //    }
            //    await ctx.RespondAsync("**Список друзей с вашей ролью**\n\n" +
            //                           $"{friendsMsg}");
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("roleadd")]
        [Description("Выдает роль донатера.")]
        public async Task RoleAdd(CommandContext ctx)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.WantedPrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
                return;
            }

            await ctx.Member.GrantRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно выдана роль донатера!");

            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    await ctx.Member.GrantRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно выдана роль донатера!");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id))
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];

            //    if (Donator.Donators[ctx.Member.Id].Balance < prices.WantedPrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //        return;
            //    }

            //    await ctx.Member.GrantRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно выдана роль донатера!");
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("rolerm")]
        [Description("Убирает роль донатера.")]
        public async Task RoleRemove(CommandContext ctx)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.Balance < prices.WantedPrice)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
                return;
            }

            await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно снята роль донатера!");

            //if (Subscriber.Subscribers.ContainsKey(ctx.Member.Id))
            //{
            //    await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно снята роль донатера!");
            //}
            //else if (Donator.Donators.ContainsKey(ctx.Member.Id))
            //{
            //    var prices = PriceList.Prices[PriceList.GetLastDate(Donator.Donators[ctx.Member.Id].Date)];

            //    if (Donator.Donators[ctx.Member.Id].Balance < prices.WantedPrice)
            //    {
            //        await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} К сожалению, эта функция недоступна вам из-за низкого баланса.");
            //        return;
            //    }

            //    await ctx.Member.RevokeRoleAsync(ctx.Guild.GetRole(Bot.BotSettings.DonatorRole));
            //    await ctx.RespondAsync($"{Bot.BotSettings.OkEmoji} Успешно снята роль донатера!");
            //}
            //else
            //{
            //    await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
            //}
        }

        [Command("sethidden")]
        [Description("Скрывает пользователя в списке донатеров")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task SetHidden(CommandContext ctx, DiscordMember member, bool hidden = true)
        {
            var donator = DonatorSQL.GetById(ctx.Member.Id);
            if (donator == null)
            {
                await ctx.RespondAsync($"{Bot.BotSettings.ErrorEmoji} Вы не являетесь донатером!");
                return;
            }

            donator.IsHidden = hidden;
        }

        [Command("genlist")]
        [Description("Генерирует список донатеров")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task GenerateList(CommandContext ctx)
        {
            var channel = ctx.Guild.GetChannel(Bot.BotSettings.DonatorChannel);

            try
            {
                await channel.DeleteMessagesAsync(await channel.GetMessagesAsync(100));
            }
            catch (NotFoundException)
            {
                // выбрасывается, если нет сообщений в канале
            }

            var donators = new Dictionary<ulong, double>(); //список донатеров, который будем сортировать
            foreach (var donator in DonatorSQL.GetAllDonators())
                if (!donator.IsHidden)
                    donators.Add(donator.UserId, donator.Balance);

            var ordered = donators.OrderBy(x => -x.Value);

            int position = 0, balance = int.MaxValue, str = 1;
            var message = "";

            foreach (var el in ordered)
            {
                if (str % 10 == 0)
                {
                    var sendedMessage = await channel.SendMessageAsync(message);
                    message = "";
                }

                if ((int)Math.Floor(el.Value) < balance)
                {
                    ++position;
                    balance = (int)Math.Floor(el.Value);
                }

                try
                {
                    var user = await ctx.Client.GetUserAsync(el.Key);
                    message += $"**{position}.** {user.Username}#{user.Discriminator} — {el.Value}₽\n";
                    ++str;
                }
                catch (NotFoundException)
                {
                }
            }

            if (str % 10 != 0)
            {
                var sendedMessage = await channel.SendMessageAsync(message);
            }
        }

        [Command("restorelostdatafromfile")]
        [Description("Сверяет пользователей сервера с сохраненным списком донатеров")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task RestoreLostDataFromFile(CommandContext ctx, String filePath)
        {
            Console.WriteLine("Started restoring lost donators");
            var members = await ctx.Guild.GetAllMembersAsync();

            string[] lines = File.ReadAllLines(filePath);

            Dictionary<DiscordMember, int> foundUsers = new Dictionary<DiscordMember, int>();

            foreach (var row in lines)
            {
                try
                {
                    var balance = Convert.ToInt32(row.Split('—')[1].Trim().TrimEnd('₽'));
                    var match = members.First(x => row.Contains($"{x.Username}#{x.Discriminator}"));
                    foundUsers.Add(match, balance);
                    Console.WriteLine($"{match} - {balance}");
                }
                catch (InvalidOperationException) { }
            }

            using (StreamWriter file = new StreamWriter("data/restoreddonators.csv"))
                foreach (var entry in foundUsers)
                    file.WriteLine($"{entry.Key.Id},{entry.Value}");

            using (StreamWriter file = new StreamWriter("data/restoreddonators.txt"))
                foreach (var entry in foundUsers)
                    file.WriteLine($"{entry.Key} - {entry.Value}");

            Console.WriteLine($"Total {foundUsers.Count} users found");
        }

        [Command("restorelostdonators")]
        [Description("Сверяет пользователей сервера с сохраненным списком донатеров")]
        [RequirePermissions(Permissions.Administrator)]
        public async Task RestoreLostDonatorsFromFile(CommandContext ctx, String filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            List<string> errored = new List<string>();
            var linesCount = lines.Count();
            for (int i = 0; i < linesCount; i++)
            {
                var values = lines[i].Split(',');
                Console.WriteLine($"Restoring donator {i} of {linesCount}");
                await ctx.RespondAsync($"Restoring donator {values[0]} {values[1]}");
                try
                {
                    var command = $"d add {values[0]} {values[1]}";

                    var cmds = ctx.CommandsNext;

                    // Ищем команду и извлекаем параметры.
                    var cmd = cmds.FindCommand(command, out var customArgs);

                    // Создаем фейковый контекст команды.
                    var fakeContext = cmds.CreateFakeContext(ctx.Member, ctx.Channel, command, ctx.Prefix, cmd, customArgs);

                    // Выполняем команду за пользователя.
                    await cmds.ExecuteCommandAsync(fakeContext);
                }
                catch (Exception ex)
                {
                    errored.Add(lines[i]);
                    await ctx.RespondAsync($"Error when restoring {values[0]} {values[1]} {ex.Message}");
                }

                await Task.Delay(5000);
            }

            File.WriteAllLines($"errored_{filePath}", errored);
        }

        private List<DiscordRole> GetColorRolesIds(DiscordGuild guild)
        {
            if (!File.Exists("generated/color_roles.txt"))
                return null;

            var colorRoles = new List<DiscordRole>();
            using (var fs = new FileStream("generated/color_roles.txt", FileMode.Open))
            using (var sr = new StreamReader(fs))
            {
                var roleId = sr.ReadLine();
                while (roleId != null)
                {
                    colorRoles.Add(guild.GetRole(Convert.ToUInt64(roleId)));
                    roleId = sr.ReadLine();
                }
            }

            return colorRoles;
        }

        public static async Task<DiscordRole> GetDonatorRoleOrCreate(DiscordGuild guild, DiscordMember member, DonatorSQL donator)
        {
            DiscordRole role;

            if (donator.PrivateRole != 0)
            {
                role = guild.GetRole(donator.PrivateRole);
            }
            else
            {
                role = await guild.CreateRoleAsync($"{member.DisplayName} Style");
                await member.GrantRoleAsync(role);
                await guild.Roles[role.Id].ModifyPositionAsync(guild.Roles[Bot.BotSettings.DonatorSpacerRole].Position - 1);
                donator.PrivateRole = role.Id;
            }

            donator.PrivateRole = role.Id;
            return role;
        }

        private bool CheckPermisson(DonatorSQL donator, DonatorPermissions permission)
        {
            var prices = DonatorPriceListSQL.Prices[DonatorPriceListSQL.GetLastDate(donator.Date)];

            if (donator.IsSubscriber())
                return true;

            switch (permission)
            {
                case DonatorPermissions.Color:
                    if (donator.Balance >= prices.ColorPrice)
                        return true;
                    break;
                case DonatorPermissions.Wanted:
                    if (donator.Balance >= prices.WantedPrice)
                        return true;
                    break;
                case DonatorPermissions.Role:
                    if (donator.Balance >= prices.RolePrice)
                        return true;
                    break;
                case DonatorPermissions.Friends:
                    if (donator.Balance >= prices.FriendsPrice)
                        return true;
                    break;
            }
            return false;
        }

        private enum DonatorPermissions
        {
            Color, Wanted, Role, Friends
        }
    }
}
