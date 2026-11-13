using OneLauncher.Core.Global;
using OneLauncher.Core.Global.ModelDataMangers;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace OneLauncher.Core.Global.ModelDataMangers;
[JsonSerializable(typeof(YggdrasilInfo))]
[JsonSerializable(typeof(UserModel))]
[JsonSerializable(typeof(AccountData))]
[JsonSerializable(typeof(Dictionary<Guid,UserModel>))]
public partial class AccountDataJsonContext : JsonSerializerContext { }
public class AccountData : IJsonOnDeserialized
{
    public AccountData() {
        UserDictionary = new Dictionary<Guid, UserModel>();
    }
    public Dictionary<Guid, UserModel> UserDictionary { get; set; }
    public Guid? DefaultUserID { get; set; }
    public void OnDeserialized()
    {
        foreach (var kvp in UserDictionary)
        {
            if (kvp.Value != null)
            {
                kvp.Value.UserID = kvp.Key;
            }
        }
    }
}
