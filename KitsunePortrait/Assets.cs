using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KitsunePortrait
{
    public static class Assets
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>
        /// Грузит PNG из папки Assets мода (Mods/KitsunePortrait/Assets/fileName) и
        /// кэширует результат — повторные вызовы с тем же именем файла не трогают диск.
        /// </summary>
        public static Sprite LoadCustomSprite(string fileName)
        {
            if (_cache.TryGetValue(fileName, out Sprite cached) && cached != null)
            {
                return cached;
            }

            string filePath = Path.Combine(Main.ModEntry.Path, "Assets", fileName);

            if (!File.Exists(filePath))
            {
                Main.Logger?.Error($"[KitsuneUI] Файл ассета не найден: {filePath}");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(filePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!ImageConversion.LoadImage(tex, fileData))
            {
                Main.Logger?.Error($"[KitsuneUI] Не удалось декодировать PNG: {filePath}");
                return null;
            }

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            _cache[fileName] = sprite;
            return sprite;
        }
    }
}