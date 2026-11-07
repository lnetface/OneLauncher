using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OneLauncher.Views.Panes.PaneViewModels;

public sealed class GameDataAccountOption
{
    public required string DisplayName { get; init; }
    public Guid? UserId { get; init; }
    public UserModel? User { get; init; }

    public bool IsGlobalDefault => UserId is null;

    public static List<GameDataAccountOption> Build(IEnumerable<UserModel> users)
    {
        var options = new List<GameDataAccountOption>
        {
            new() { DisplayName = "使用全局默认账户" }
        };
        options.AddRange(users.Select(user => new GameDataAccountOption
        {
            DisplayName = user.Name,
            UserId = user.UserID,
            User = user
        }));
        return options;
    }
}
