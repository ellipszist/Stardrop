﻿using SharpCompress.Archives;
using Stardrop.Models;
using Stardrop.Models.Data;
using Stardrop.Models.Nexus;
using Stardrop.Models.Nexus.Web;
using Stardrop.Models.SMAPI;
using Stardrop.Models.SMAPI.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Stardrop.Utilities.External
{
    static class Nexus
    {
        internal static int dailyRequestsRemaining;
        internal static int dailyRequestsLimit;

        private static Uri _baseUrl = new Uri("http://api.nexusmods.com/v1/");

        // Regex for extracting required components for Nexus file downloading: 
        // nxm:\/\/(?<domain>stardewvalley)\/mods\/(?<mod>[0-9]+)\/files\/(?<file>[0-9]+)\?key=(?<key>[0-9]+)&expires=(?<expiry>[0-9]+)&user_id=(?<user>[0-9]+)
        //https://app.swaggerhub.com/apis-docs/NexusMods/nexus-mods_public_api_params_in_form_data/1.0#/Mod%20Files/get_v1_games_game_domain_mods_mod_id_files_id_download_link.json
        public static string? GetKey()
        {
            if (Program.settings.NexusDetails is null || Program.settings.NexusDetails.Key is null || File.Exists(Pathing.GetNotionCachePath()) is false)
            {
                return null;
            }

            var pairedKeys = JsonSerializer.Deserialize<PairedKeys>(File.ReadAllText(Pathing.GetNotionCachePath()), new JsonSerializerOptions { AllowTrailingCommas = true });
            if (pairedKeys is null || pairedKeys.Vector is null || pairedKeys.Vector is null)
            {
                return null;
            }

            return SimpleObscure.Decrypt(Program.settings.NexusDetails.Key, pairedKeys.Lock, pairedKeys.Vector);
        }

        public async static Task<bool> ValidateKey(string key)
        {
            // Create a throwaway client
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", key);
            client.DefaultRequestHeaders.Add("Application-Name", "Stardrop");
            client.DefaultRequestHeaders.Add("Application-Version", Program.applicationVersion);

            bool wasValidated = true;
            try
            {
                var response = await client.GetAsync(new Uri(_baseUrl, "users/validate"));
                if (response.StatusCode == System.Net.HttpStatusCode.OK && response.Content is not null)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    Validate validationModel = JsonSerializer.Deserialize<Validate>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (validationModel is null || String.IsNullOrEmpty(validationModel.Message) is false)
                    {
                        Program.helper.Log($"Unable to validate given API key for Nexus Mods");
                        Program.helper.Log($"Response from Nexus Mods:\n{content}");

                        wasValidated = false;
                    }
                    else if (Program.settings.NexusDetails is not null)
                    {
                        Program.settings.NexusDetails.Username = validationModel.Name;

                        UpdateRequestCounts(response.Headers);
                    }
                }
                else
                {
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Program.helper.Log($"Bad status given from Nexus Mods: {response.StatusCode}");
                        if (response.Content is not null)
                        {
                            Program.helper.Log($"Response from Nexus Mods:\n{await response.Content.ReadAsStringAsync()}");
                        }
                    }
                    else if (response.Content is null)
                    {
                        Program.helper.Log($"No response from Nexus Mods!");
                    }

                    wasValidated = false;
                }
            }
            catch (Exception ex)
            {
                Program.helper.Log($"Failed to validate user's API key for Nexus Mods: {ex}", Helper.Status.Alert);
                wasValidated = false;
            }
            client.Dispose();

            return wasValidated;
        }

        private static void UpdateRequestCounts(HttpResponseHeaders headers)
        {
            if (headers.TryGetValues("x-rl-daily-limit", out var limitValues) && Int32.TryParse(limitValues.First(), out int dailyLimit))
            {
                dailyRequestsLimit = dailyLimit;
            }

            if (headers.TryGetValues("x-rl-daily-remaining", out var remainingValues) && Int32.TryParse(remainingValues.First(), out int dailyRemaining))
            {
                dailyRequestsRemaining = dailyRemaining;
            }
        }
    }
}