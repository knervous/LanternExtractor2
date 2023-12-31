﻿using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LanternExtractor
{
    public static class EqFileHelper
    {
        public static List<string> GetValidEqFilePaths(string directory, string archiveName)
        {
            archiveName = archiveName.ToLower();
            var eqFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            List<string> validFiles;

            switch (archiveName)
            {
                case "all":
                    validFiles = GetAllValidFiles(eqFiles);
                    break;
                case "zones":
                    validFiles = GetValidZoneFiles(eqFiles);
                    break;
                case "characters":
                    validFiles = GetValidCharacterFiles(eqFiles);
                    break;
                case "equipment":
                    validFiles = GetValidEquipmentFiles(eqFiles);
                    break;
                case "sounds":
                    validFiles = GetValidSoundFiles(eqFiles);
                    break;
                default:
                {
                    validFiles = GetValidFiles(archiveName, directory);
                    break;
                }
            }

            return validFiles;
        }

        private static List<string> GetValidEquipmentFiles(string[] eqFiles)
        {
            return eqFiles.Where(x => IsEquipmentArchive(Path.GetFileName(x))).ToList();
        }

        private static List<string> GetAllValidFiles(string[] eqFiles)
        {
            return eqFiles.Where(x => IsValidArchive(Path.GetFileName(x))).ToList();
        }

        private static List<string> GetValidZoneFiles(string[] eqFiles)
        {
            return eqFiles.Where(x => IsZoneArchive(Path.GetFileName(x))).ToList();
        }

        private static List<string> GetValidCharacterFiles(string[] eqFiles)
        {
            return eqFiles.Where(x => IsCharacterArchive(Path.GetFileName(x))).ToList();
        }

        private static List<string> GetValidSoundFiles(string[] eqFiles)
        {
            return eqFiles.Where(x => IsSoundArchive(Path.GetFileName(x))).ToList();
        }

        private static List<string> GetValidFiles(string archiveName, string directory)
        {
            var validFiles = new List<string>();
            if (archiveName.EndsWith(".s3d") || archiveName.EndsWith(".pfs"))
            {
                string archivePath = Path.Combine(directory, archiveName);
                if (File.Exists(archivePath))
                {
                    validFiles.Add(archivePath);
                }
            }
            else
            {
                string archivePath = Path.Combine(directory, $"{archiveName}.pfs");

                if (File.Exists(archivePath))
                {
                    validFiles.Add(archivePath);
                    return validFiles;
                }

                // Try and find all associated files with the shortname - can theoretically be a non-zone file
                string mainArchivePath = Path.Combine(directory, $"{archiveName}.s3d");
                if (File.Exists(mainArchivePath))
                {
                    validFiles.Add(mainArchivePath);
                }

                // Some zones have additional object archives for things added past their initial release
                // No archives contain cross archive fragment references
                string extensionObjectsArchivePath = Path.Combine(directory, $"{archiveName}_2_obj.s3d");
                if (File.Exists(extensionObjectsArchivePath))
                {
                    validFiles.Add(extensionObjectsArchivePath);
                }

                string objectsArchivePath = Path.Combine(directory, $"{archiveName}_obj.s3d");
                if (File.Exists(objectsArchivePath))
                {
                    validFiles.Add(objectsArchivePath);
                }

                string charactersArchivePath = Path.Combine(directory, $"{archiveName}_chr.s3d");
                if (File.Exists(charactersArchivePath))
                {
                    validFiles.Add(charactersArchivePath);
                }

                // Some zones have additional character archives for things added past their initial release
                // None of them contain fragments that are linked to other related archives.
                // "qeynos" must be excluded because both qeynos and qeynos2 are used as shortnames
                string extensionCharactersArchivePath = Path.Combine(directory, $"{archiveName}2_chr.s3d");
                if (File.Exists(extensionCharactersArchivePath) && archiveName != "qeynos")
                {
                    validFiles.Add(extensionCharactersArchivePath);
                }
            }

            return validFiles;
        }

        private static bool IsValidArchive(string archiveName)
        {
            return archiveName.EndsWith(".s3d") || archiveName.EndsWith(".pfs") && !archiveName.Contains("chequip") &&
                !archiveName.EndsWith("_lit.s3d");
        }

        private static bool IsZoneArchive(string archiveName)
        {
            return IsValidArchive(archiveName) && !IsEquipmentArchive(archiveName) && !IsSkyArchive(archiveName) &&
                   !IsBitmapArchive(archiveName) && !IsCharacterArchive(archiveName);
        }

        public static bool IsEquipmentArchive(string archiveName)
        {
            return archiveName.StartsWith("gequip");
        }

        public static bool IsCharacterArchive(string archiveName)
        {
            return archiveName.Contains("_chr") || archiveName.StartsWith("chequip") ||
                                                   archiveName.Contains("_amr");
        }

        public static bool IsObjectsArchive(string archiveName)
        {
            return archiveName.Contains("_obj");
        }

        public static bool IsSkyArchive(string archiveName)
        {
            return archiveName == "sky";
        }

        public static bool IsBitmapArchive(string archiveName)
        {
            return archiveName.StartsWith("bmpwad");
        }

        public static bool IsSoundArchive(string archiveName)
        {
            return archiveName.StartsWith("snd");
        }

        public static bool IsClientDataFile(string archiveName)
        {
            return archiveName == "clientdata";
        }

        public static bool IsMusicFile(string filename)
        {
            return filename.EndsWith(".xmi");
        }

        public static bool IsSpecialCaseExtraction(string archiveName)
        {
            return archiveName == "clientdata" || archiveName == "music";
        }

        public static bool IsUsedSoundArchive(string archiveName)
        {
            if (!IsSoundArchive(archiveName))
            {
                return false;
            }

            // Trilogy client does not use archives higher than snd9
            if (int.TryParse(archiveName.Substring(archiveName.Length - 2), out _))
            {
                return false;
            }

            return true;
        }
    }
}