﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Comfort.Common;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Trainer.Configuration;
using EFT.Trainer.Extensions;
using EFT.UI;
using JsonType;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EFT.Trainer.Features
{
	public class Commands : MonoBehaviour
	{
		public bool Registered { get; set; } = false;
		public const string ValueGroup = "value";
		public string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Escape from Tarkov");
		private readonly Dictionary<string, Type> _features = new()
		{
			{"wallhack", typeof(Players)},
			{"stash", typeof(LootableContainers)},
			{"stamina", typeof(Stamina)},
			{"quest", typeof(Quests)},
			{"norecoil", typeof(Recoil)},
			{"loot", typeof(LootItems)},
			{"hud", typeof(Hud)},
			{"exfil", typeof(ExfiltrationPoints)},
			{"autogun", typeof(AutomaticGun)},
		};

		private void Update()
		{
			if (Registered)
				return;

			if (!PreloaderUI.Instantiated)
				return;

			RegisterCommands();
		}

		private void RegisterCommands()
		{
			var commands = ConsoleScreen.Commands;
			if (commands.Count == 0)
				return;

			foreach (var (featureName, featureType) in _features)
			{
				commands.AddCommand(new GClass1907($"{featureName} (?<{ValueGroup}>(on)|(off))", m => OnTriggerFeature(featureType, m)));
			}

			commands.AddCommand(new GClass1907("dump", _ => Dump()));
			commands.AddCommand(new GClass1907("status", _ => Status()));

			var feature = Loader.HookObject.GetComponent<LootItems>();
			if (feature != null)
			{
				commands.AddCommand(new GClass1907($"list {{0}}( (?<{ValueGroup}>.*))?", m => ListLootItems(m, feature)));
				commands.AddCommand(new GClass1907($"listr {{0}}( (?<{ValueGroup}>.*))?", m => ListLootItems(m, feature, ELootRarity.Rare)));
				commands.AddCommand(new GClass1907($"listsr {{0}}( (?<{ValueGroup}>.*))?", m => ListLootItems(m, feature, ELootRarity.Superrare)));

				commands.AddCommand(new GClass1907($"track (?<{ValueGroup}>.*)", m => TrackLootItem(m, feature)));
				commands.AddCommand(new GClass1907($"untrack (?<{ValueGroup}>.*)", m => UnTrackLootItem(m, feature)));
				commands.AddCommand(new GClass1907("tracklist", _ => ShowTrackList(feature)));
			}

			var configFile = Path.Combine(UserPath, "trainer.ini");
			var features = Loader.HookObject.GetComponents(typeof(MonoBehaviour));
			commands.AddCommand(new GClass1907("load", _ => ConfigurationManager.Load(configFile, features)));
			commands.AddCommand(new GClass1907("save", _ => ConfigurationManager.Save(configFile, features)));

			// Load default configuration
			ConfigurationManager.Load(configFile, features, false);

			Registered = true;
			Destroy(this);
		}

		private static void ShowTrackList(LootItems feature, bool changed = false)
		{
			if (changed)
				AddConsoleLog("Tracking list updated...", "tracker");

			foreach (var item in feature.TrackedNames)
				AddConsoleLog($"Tracking: {item}", "tracker");
		}

		private static void UnTrackLootItem(Match match, LootItems feature)
		{
			var matchGroup = match?.Groups[ValueGroup];
			if (matchGroup == null || !matchGroup.Success)
				return;

			ShowTrackList(feature, feature.UnTrack(matchGroup.Value));
		}

		private static void TrackLootItem(Match match, LootItems feature)
		{
			var matchGroup = match?.Groups[ValueGroup];
			if (matchGroup == null || !matchGroup.Success)
				return;

			ShowTrackList(feature, feature.Track(matchGroup.Value));
		}

		private static void ListLootItems(Match match, LootItems feature, ELootRarity rarityFilter = ELootRarity.Not_exist)
		{
			var search = string.Empty;
			var matchGroup = match?.Groups[ValueGroup];
			if (matchGroup != null && matchGroup.Success)
				search = matchGroup.Value.Trim();

			var world = Singleton<GameWorld>.Instance;

			if (world == null)
				return;

			var itemsPerName = new Dictionary<string, List<Item>>();

			// Step 1 - look outside containers (loot items)
			FindLootItems(world, itemsPerName);

			// Step 2 - look inside containers (items)
			if (feature.SearchInsideContainers)
				FindItemsInContainers(itemsPerName);

			var names = itemsPerName.Keys.ToList();
			names.Sort();
			names.Reverse();

			var count = 0;
			foreach (var itemName in names)
			{
				if (itemName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
					continue;

				var list = itemsPerName[itemName];
				var rarity = list.First().Template.Rarity;
				if (rarityFilter != ELootRarity.Not_exist && rarityFilter != rarity)
					continue;

				var extra = rarity != ELootRarity.Not_exist ? $" ({rarity.Color()})" : string.Empty;
				AddConsoleLog($"{itemName} [{list.Count}]{extra}", "list");

				count += list.Count;
			}

			AddConsoleLog("------", "list");
			AddConsoleLog($"found {count} items", "list");
		}

		private static void FindItemsInContainers(Dictionary<string, List<Item>> itemsPerName)
		{
			var containers = FindObjectsOfType<LootableContainer>();
			foreach (var container in containers)
			{
				if (!container.IsValid())
					continue;

				var items = container
					.ItemOwner?
					.RootItem?
					.GetAllItems()?
					.ToArray();

				if (items == null)
					continue;

				IndexItems(items, itemsPerName);
			}
		}

		private static void FindLootItems(GameWorld world, Dictionary<string, List<Item>> itemsPerName)
		{
			var lootItems = world.LootItems;
			var filteredItems = new List<Item>();
			for (var i = 0; i < lootItems.Count; i++)
			{
				var lootItem = lootItems.GetByIndex(i);
				if (!lootItem.IsValid())
					continue;

				filteredItems.Add(lootItem.Item);
			}

			IndexItems(filteredItems, itemsPerName);
		}

		private static void IndexItems(IEnumerable<Item> items, Dictionary<string, List<Item>> itemsPerName)
		{
			foreach (var item in items)
			{
				if (!item.IsValid())
					continue;

				var itemName = item.ShortName.Localized();
				if (!itemsPerName.TryGetValue(itemName, out var pnList))
				{
					pnList = new List<Item>();
					itemsPerName[itemName] = pnList;
				}

				pnList.Add(item);
			}
		}

		private void Status()
		{
			foreach (var (featureName, featureType) in _features)
			{
				if (Loader.HookObject.GetComponent(featureType) is not FeatureMonoBehaviour feature)
				{
					AddConsoleLog($"{featureName} is not loaded!");
					continue;
				}

				var help = feature.Key != KeyCode.None ? $" ({feature.Key} to toggle)" : string.Empty;
				AddConsoleLog($"{featureName} is {(feature.Enabled ? "on".Green() : "off".Red())}{help}", "status");
			}
		}

		private void Dump()
		{
			var dumpfolder = Path.Combine(UserPath, "Dumps");
			var thisDump = Path.Combine(dumpfolder, $"{DateTime.Now:yyyyMMdd-HHmmss}");

			Directory.CreateDirectory(thisDump);

			AddConsoleLog("Dumping scenes...", "dump");
			for (int i = 0; i < SceneManager.sceneCount; i++) 
			{
				var scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded)
					continue;

				var json = SceneDumper.DumpScene(scene).ToPrettyJson();
				File.WriteAllText(Path.Combine(thisDump, GetSafeFilename($"@scene - {scene.name}.txt")), json);
			}

			AddConsoleLog("Dumping game objects...", "dump");
			foreach (var go in FindObjectsOfType<GameObject>())
			{
				if (go == null || go.transform.parent != null || !go.activeSelf) 
					continue;

				var filename = GetSafeFilename(go.name + "-" + go.GetHashCode() + ".txt");
				var json = SceneDumper.DumpGameObject(go).ToPrettyJson();
				File.WriteAllText(Path.Combine(thisDump, filename), json);
			}

			AddConsoleLog($"Dump created in {thisDump}", "dump");
		}

		private static string GetSafeFilename(string filename)
		{
			return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));  	
		}

		public void OnTriggerFeature(Type featureType, Match match)
		{
			var matchGroup = match?.Groups[ValueGroup];
			if (matchGroup == null || !matchGroup.Success)
				return;

			if (Loader.HookObject.GetComponent(featureType) is not FeatureMonoBehaviour feature)
				return;

			feature.Enabled = matchGroup.Value switch
			{
				"on" => true,
				"off" => false,
				_ => feature.Enabled
			};
		}

		private static void AddConsoleLog(string log, string from = "scheduler")
		{
			if (PreloaderUI.Instantiated)
				PreloaderUI.Instance.Console.AddLog(log, from);
		}
	}
}
