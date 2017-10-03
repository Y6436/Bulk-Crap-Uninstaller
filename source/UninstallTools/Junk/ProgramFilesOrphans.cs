﻿/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Klocman.Extensions;
using Klocman.Tools;
using UninstallTools.Junk.Confidence;
using UninstallTools.Junk.Containers;
using UninstallTools.Properties;

namespace UninstallTools.Junk
{
    public class ProgramFilesOrphans : IJunkCreator
    {
        public static readonly ConfidenceRecord ConfidenceEmptyFolder = new ConfidenceRecord(4,
            Localisation.Confidence_PF_EmptyFolder);

        public static readonly ConfidenceRecord ConfidenceExecsPresent = new ConfidenceRecord(-4,
            Localisation.Confidence_PF_ExecsPresent);

        public static readonly ConfidenceRecord ConfidenceFilesPresent = new ConfidenceRecord(0,
            Localisation.Confidence_PF_FilesPresent);

        public static readonly ConfidenceRecord ConfidenceManyFilesPresent = new ConfidenceRecord(-2,
            Localisation.Confidence_PF_ManyFilesPresent);

        public static readonly ConfidenceRecord ConfidenceNameIsUsed = new ConfidenceRecord(-4,
            Localisation.Confidence_PF_NameIsUsed);

        public static readonly ConfidenceRecord ConfidenceNoSubdirs = new ConfidenceRecord(2,
            Localisation.Confidence_PF_NoSubdirs);

        public static readonly ConfidenceRecord ConfidencePublisherIsUsed = new ConfidenceRecord(-4,
            Localisation.Confidence_PF_PublisherIsUsed);

        private string[] _otherInstallLocations;
        private string[] _otherNames;
        private string[] _otherPublishers;
        private IEnumerable<KeyValuePair<DirectoryInfo, bool?>> _programFilesDirectories;

        public IEnumerable<IJunkResult> FindJunk(ApplicationUninstallerEntry target)
        {
            // Do nothing when called by the manager
            yield break;
        }

        public IEnumerable<IJunkResult> FindAllJunk()
        {
            var output = new List<FileSystemJunk>();

            foreach (var kvp in _programFilesDirectories)
                FindJunkRecursively(output, kvp.Key, 0);

            return output.Cast<IJunkResult>();
        }

        private void FindJunkRecursively(ICollection<FileSystemJunk> returnList, DirectoryInfo parentDirectory, int level)
        {
            try
            {
                if ((parentDirectory.Attributes & FileAttributes.System) == FileAttributes.System)
                    return;

                var subDirectories = parentDirectory.GetDirectories();

                foreach (var subDirectory in subDirectories)
                {
                    if (UninstallToolsGlobalConfig.IsSystemDirectory(subDirectory))
                        continue;

                    if (subDirectory.FullName.ContainsAny(_otherInstallLocations, StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    var questionableDirName = subDirectory.Name.ContainsAny(
                        UninstallToolsGlobalConfig.QuestionableDirectoryNames, StringComparison.CurrentCultureIgnoreCase)
                                              ||
                                              UninstallToolsGlobalConfig.QuestionableDirectoryNames.Any(
                                                  x => x.Contains(subDirectory.Name, StringComparison.CurrentCultureIgnoreCase));

                    var nameIsUsed = subDirectory.Name.ContainsAny(_otherNames, StringComparison.CurrentCultureIgnoreCase);

                    var allFiles = subDirectory.GetFiles("*", SearchOption.AllDirectories);
                    var allFilesContainExe = allFiles.Any(x => WindowsTools.IsExectuable(x.Extension, false, true));
                    var immediateFiles = subDirectory.GetFiles("*", SearchOption.TopDirectoryOnly);

                    ConfidenceRecord resultRecord;

                    if (immediateFiles.Any())
                    {
                        // No executables, MAYBE safe to remove
                        // Executables present, bad idea to remove
                        resultRecord = allFilesContainExe ? ConfidenceExecsPresent : ConfidenceFilesPresent;
                    }
                    else if (!allFiles.Any())
                    {
                        // Empty folder, safe to remove
                        resultRecord = ConfidenceEmptyFolder;
                    }
                    else
                    {
                        // This folder is empty, but insides contain stuff
                        resultRecord = allFilesContainExe ? ConfidenceExecsPresent : ConfidenceFilesPresent;

                        if (level < 1 && !questionableDirName && !nameIsUsed)
                        {
                            FindJunkRecursively(returnList, subDirectory, level + 1);
                        }
                    }

                    if (resultRecord == null) continue;

                    var newNode = new FileSystemJunk(subDirectory, null, this);
                    newNode.Confidence.Add(resultRecord);

                    if (subDirectory.Name.ContainsAny(_otherPublishers, StringComparison.CurrentCultureIgnoreCase))
                        newNode.Confidence.Add(ConfidencePublisherIsUsed);

                    if (nameIsUsed)
                        newNode.Confidence.Add(ConfidenceNameIsUsed);

                    if (questionableDirName)
                        newNode.Confidence.Add(ConfidenceRecord.QuestionableDirectoryName);

                    if (allFiles.Length > 100)
                        newNode.Confidence.Add(ConfidenceManyFilesPresent);

                    // Remove 2 points for every sublevel
                    newNode.Confidence.Add(level * -2);

                    if (!subDirectory.GetDirectories().Any())
                        newNode.Confidence.Add(ConfidenceNoSubdirs);

                    returnList.Add(newNode);
                }
            }
            catch (Exception ex)
            {
                if (Debugger.IsAttached) throw;
                Console.WriteLine(ex);
            }
        }

        public void Setup(ICollection<ApplicationUninstallerEntry> allUninstallers)
        {
            _programFilesDirectories = UninstallToolsGlobalConfig.GetProgramFilesDirectories(true);

            var applicationUninstallerEntries = allUninstallers as IList<ApplicationUninstallerEntry> ?? allUninstallers.ToList();

            _otherInstallLocations =
                applicationUninstallerEntries.SelectMany(x => new[] { x.InstallLocation, x.UninstallerLocation })
                    .Where(x => x.IsNotEmpty()).Distinct().ToArray();

            _otherPublishers =
                applicationUninstallerEntries.Select(x => x.PublisherTrimmed).Where(x => x != null && x.Length > 3)
                    .Distinct().ToArray();
            _otherNames =
                applicationUninstallerEntries.Select(x => x.DisplayNameTrimmed).Where(x => x != null && x.Length > 3)
                    .Distinct().ToArray();
        }

        public string CategoryName => Localisation.Junk_ProgramFilesOrphans_GroupName;
    }
}