﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using Microsoft.Win32;

namespace BakeryEngine
{
    /// <summary>
    /// Implementation of commands
    /// </summary>
    public partial class BakeryEngine
    {
        public enum IfConditionSubOpcode
        {
            None = 0,
            ExistFile, ExistDir, ExistSection, ExistRegSection, ExistRegKey, ExistVar,
            Equal, Smaller, Bigger, SmallerEqual, BiggerEqual,
            Not, 
            Online, Question, ExistMacro,
            // Deprecated
            EqualX, Ping, License, ExistRegMulti
        }

        /// <summary>
        /// Run,%PluginFile%,<Section>[,PARAMS]
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public LogInfo[] RunExec(BakeryCommand cmd)
        {
            return InternalRunExec(cmd, false, 0);
        }

        private LogInfo[] RunExecCallback(BakeryCommand cmd, int depth)
        {
            return InternalRunExec(cmd, true, depth);
        }

        private LogInfo[] InternalRunExec(BakeryCommand cmd, bool callback, int depth)
        {
            List<LogInfo> logs = new List<LogInfo>();

            try
            {
                // Necessary operand : 2, optional operand : variable length
                const int necessaryOperandNum = 2;
                if (cmd.Operands.Length < necessaryOperandNum)
                    throw new InvalidOperandException("Necessary operands does not exist", cmd);

                // Get necesssary operand
                string pluginFile = EscapeString(variables.Expand(cmd.Operands[0]));
                string sectionName = EscapeString(variables.Expand(cmd.Operands[1]));
                string rawPluginFile = cmd.Operands[0];
                string rawSectoinName = cmd.Operands[1];

                // Get optional operand 
                string[] parameters = new string[cmd.Operands.Length - necessaryOperandNum];
                if (necessaryOperandNum < cmd.Operands.Length)
                    Array.Copy(cmd.Operands, 2, parameters, 0, cmd.Operands.Length - necessaryOperandNum);

                bool inCurrentPlugin = false;
                if (string.Equals(rawPluginFile, "%PluginFile%", StringComparison.OrdinalIgnoreCase))
                    inCurrentPlugin = true;
                else if (string.Equals(rawPluginFile, "%ScriptFile%", StringComparison.OrdinalIgnoreCase))
                    inCurrentPlugin = true;

                Plugin targetPlugin;
                if (inCurrentPlugin)
                    targetPlugin = currentPlugin;
                else
                    targetPlugin = project.ActivePlugins.SearchByFullPath(variables.Expand(pluginFile));

                // Does section exists?
                if (!targetPlugin.Sections.ContainsKey(sectionName))
                    throw new InvalidOperandException($"[{rawPluginFile}] does not have section [{sectionName}]", cmd);

                // Branch to new section
                CommandAddress nextAddr = new CommandAddress(); // Blank value
                if (callback)
                {
                    nextAddr = new CommandAddress(targetPlugin, targetPlugin.Sections[sectionName], 0, targetPlugin.Sections[sectionName].Count);
                    if (depth != 0)
                    {
                        if (inCurrentPlugin)
                            logger.Write(new LogInfo(cmd, LogState.Success, $"Processing section [{sectionName}]"), true);
                        else
                            logger.Write(new LogInfo(cmd, LogState.Success, $"Processing [{rawPluginFile}]'s section [{sectionName}]"), true);
                    }
                }
                else
                {
                    returnAddress.Push(new CommandAddress(cmd.Address.plugin, cmd.Address.section, cmd.Address.line + 1, cmd.Address.secLength));
                    nextCommand = new CommandAddress(targetPlugin, targetPlugin.Sections[sectionName], -1, targetPlugin.Sections[sectionName].Count);
                    currentSectionParams = parameters;
                    if (inCurrentPlugin)
                        logs.Add(new LogInfo(cmd, LogState.Success, $"Processing section [{sectionName}]"));
                    else
                        logs.Add(new LogInfo(cmd, LogState.Success, $"Processing [{rawPluginFile}]'s section [{sectionName}]"));
                }

                // Exec utilizes [Variables] section of the plugin
                if (cmd.Opcode == Opcode.Exec)
                    variables.AddVariables(VarsType.Local, targetPlugin.Sections["Variables"], returnAddress.Count, true);

                if (callback)
                    RunCallbackSection(nextAddr, parameters, depth);
            }
            catch (Exception e)
            {
                if (callback)
                    logs.Add(new LogInfo(cmd, LogState.Error, e.GetType() + ": " + Helper.RemoveLastNewLine(e.Message)));
                else
                    throw e;
            }

            return logs.ToArray();
        }

        /// <summary>
        /// If,<Condition>,<Command>
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public LogInfo[] IfCondition(BakeryCommand cmd)
        {
            LogInfo[] logs = new LogInfo[0]; // TODO: Delete this init sentence if If command is implemented

            // Necessary operand : 3, 2 for condition and 1 for embeded command
            const int necessaryOperandNum = 3;
            if (cmd.Operands.Length < necessaryOperandNum)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            // Get Condition SubOpcode
            IfConditionSubOpcode subOpcode = IfConditionSubOpcode.None;
            BakerySubCommand subCmd;

            // Parse opcode
            int subOpcodeIdx = 0;
            bool notFlag = false;
            string subOpcodeString = cmd.Operands[subOpcodeIdx];

            if (string.Equals(cmd.Operands[0], "Not", StringComparison.OrdinalIgnoreCase))
            {
                notFlag = true;
                subOpcodeIdx++;
            }

            // Check if subOpcodeString starts and ends with % -> Equal, Smaller, Bigger
            if (cmd.Operands[subOpcodeIdx].StartsWith("%") && cmd.Operands[subOpcodeIdx].EndsWith("%"))
            {
                if (cmd.Operands.Length < 4) // 3 for %A%,Equal,%B% and 1 for embeded command
                    throw new InvalidOperandException("Necessary operands does not exist", cmd);

                subOpcodeString = cmd.Operands[subOpcodeIdx + 1];
                if (string.Equals(subOpcodeString, "Equal", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, "==", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.Equal;
                else if (string.Equals(subOpcodeString, "Smaller", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, "<", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.Smaller;
                else if (string.Equals(subOpcodeString, "Bigger", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, ">", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.Bigger;
                else if (string.Equals(subOpcodeString, "SmallerEqual", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, "<=", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.SmallerEqual;
                else if (string.Equals(subOpcodeString, "BiggerEqual", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, ">=", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.BiggerEqual;
                else if (string.Equals(subOpcodeString, "NotEqual", StringComparison.OrdinalIgnoreCase) || string.Equals(subOpcodeString, "!=", StringComparison.OrdinalIgnoreCase))
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd);
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.Equal;
                }
                else if (string.Equals(subOpcodeString, "EqualX", StringComparison.OrdinalIgnoreCase)) // deprecated 
                    subOpcode = IfConditionSubOpcode.EqualX;
                else
                    throw new InvalidSubOpcodeException($"Invalid sub command [If,{subOpcodeString}]", cmd);

                // Ex) If,%Joveler%,Equal,ied206,Set,%A%,True
                // -> new string[] { "%Joveler%",ied206,Set,%A%,True}
                List<string> operandList = new List<string>();
                operandList.Add(cmd.Operands[0]);
                operandList.AddRange(cmd.Operands.Skip(subOpcodeIdx + 2));
                subCmd = new BakerySubCommand(SubCommandType.IfCondition, subOpcode, operandList.ToArray(), notFlag);
            }
            else
            {
                // Get condition SubOpcode string
                subOpcodeString = cmd.Operands[subOpcodeIdx];
                if (string.Equals(subOpcodeString, "ExistFile", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistFile;
                else if (string.Equals(subOpcodeString, "ExistDir", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistDir;
                else if (string.Equals(subOpcodeString, "ExistSection", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistSection;
                else if (string.Equals(subOpcodeString, "ExistRegSection", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistRegSection;
                else if (string.Equals(subOpcodeString, "ExistRegKey", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistRegKey;
                else if (string.Equals(subOpcodeString, "ExistVar", StringComparison.OrdinalIgnoreCase))
                    subOpcode = IfConditionSubOpcode.ExistVar;
                else if (string.Equals(subOpcodeString, "NotExistFile", StringComparison.OrdinalIgnoreCase))
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd); // deprecated 
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistFile;
                }
                else if (string.Equals(subOpcodeString, "NotExistDir", StringComparison.OrdinalIgnoreCase))
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd); // deprecated 
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistDir;
                }
                else if (string.Equals(subOpcodeString, "NotExistSection", StringComparison.OrdinalIgnoreCase))
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd); // deprecated 
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistSection;
                }
                else if (string.Equals(subOpcodeString, "NotExistRegSection", StringComparison.OrdinalIgnoreCase)) // deprecated
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd);
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistRegSection;
                }
                else if (string.Equals(subOpcodeString, "NotExistRegKey", StringComparison.OrdinalIgnoreCase)) // deprecated 
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd);
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistRegKey;
                }
                else if (string.Equals(subOpcodeString, "NotExistVar", StringComparison.OrdinalIgnoreCase))  // deprecated 
                {
                    if (notFlag)
                        throw new InvalidSubOpcodeException("Condition [Not] cannot be duplicated", cmd);
                    notFlag = true;
                    subOpcode = IfConditionSubOpcode.ExistVar;
                }
                else
                    throw new InvalidSubOpcodeException($"Invalid sub command [If,{subOpcodeString}]", cmd);
                subCmd = new BakerySubCommand(SubCommandType.IfCondition, subOpcode, cmd.Operands.Skip(subOpcodeIdx + 1).ToArray(), notFlag);
            }

            // Call sub command methods
            switch (subOpcode)
            {
                case IfConditionSubOpcode.ExistFile:
                    logs = this.IfExistFile(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.ExistDir:
                    logs = this.IfExistDir(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.ExistSection:
                    logs = this.IfExistSection(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.ExistRegSection:
                    logs = this.IfExistRegSection(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.ExistRegKey:
                    logs = this.IfExistRegKey(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.ExistVar:
                    logs = this.IfExistVar(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.Equal:
                case IfConditionSubOpcode.Smaller:
                case IfConditionSubOpcode.Bigger:
                case IfConditionSubOpcode.SmallerEqual:
                case IfConditionSubOpcode.BiggerEqual:
                    logs = this.IfCompare(cmd, subCmd);
                    break;
                case IfConditionSubOpcode.Online:
                    break;
                default: // In fact, Enum.Parse logic must handle this. If this logic is called in production, it is definitely a BUG
                    throw new InvalidSubOpcodeException($"INTERNAL ERROR! Invalid sub command [If,{subOpcodeString}]", cmd);
            }

            return logs;
        }

        public BakeryCommand IfInternalCommand(BakeryCommand cmd, BakerySubCommand subCmd, int opcodeIdx)
        {
            // If ExistFile,Joveler.txt,Echo,ied206
            // [subCmd]   0,           1,  2,     3 -> opcodeIdx must be 2 

            // Parse opcode
            Opcode opcode = Opcode.None;
            string opcodeStr = subCmd.Operands[opcodeIdx];
            try
            {
                opcode = (Opcode) Enum.Parse(typeof(Opcode), opcodeStr, true);
                if (!Enum.IsDefined(typeof(Opcode), opcode) || opcode == Opcode.None)
                    throw new ArgumentException();
            }
            catch (ArgumentException)
            {
                throw new InvalidOpcodeException($"Unknown command [{opcodeStr}]", cmd);
            }

            int sectionDepth = returnAddress.Count + 1;
            if (opcode == Opcode.Run)
                sectionDepth -= 1;

            return new BakeryCommand(cmd.RawCode, opcode, subCmd.Operands.Skip(opcodeIdx + 1).ToArray(), cmd.Address, sectionDepth);
        }

        /// <summary>
        /// If,ExistFile,<FilePath>,<Command>
        /// </summary>
        /// <remarks>Support wildcard</remarks>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistFile(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 1 for condition and 1 for command
            const int necessaryOperandNum = 1;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string filePath = EscapeString(variables.Expand(subCmd.Operands[0]));
            string rawFilePath = subCmd.Operands[0];

            // Check filePath contains wildcard
            bool filePathContainsWildcard = true;
            if (filePath.IndexOfAny(new char[] { '*', '?' }) == -1) // No wildcard
                filePathContainsWildcard = false;

            // Check if file exists
            bool run;
            if (filePathContainsWildcard)
            {
                string[] list = Directory.GetFiles(Helper.GetDirNameEx(filePath), Path.GetFileName(filePath));
                if (0 < list.Length)
                    run = true;
                else
                    run = false;
            }
            else
                run = File.Exists(filePath);

            string resMessage;
            if (run) // Exists
                resMessage = $"File [{rawFilePath}] exists";
            else
                resMessage = $"File [{rawFilePath}] does not exists";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            return logs.ToArray();
        }

        /// <summary>
        /// If,ExistDir,<DirPath>,<Command>
        /// </summary>
        /// <remarks>Support wildcard</remarks>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistDir(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 1 for condition and 1 for command
            const int necessaryOperandNum = 1;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string dirPath = EscapeString(variables.Expand(subCmd.Operands[0]));
            string rawFilePath = subCmd.Operands[0];

            // Check filePath contains wildcard
            bool dirPathContainsWildcard = true;
            if (dirPath.IndexOfAny(new char[] { '*', '?' }) == -1) // No wildcard
                dirPathContainsWildcard = false;

            // Check if file exists
            bool run;
            if (dirPathContainsWildcard)
            {
                string[] list = Directory.GetDirectories(Helper.GetDirNameEx(dirPath), Path.GetFileName(dirPath));
                if (0 < list.Length)
                    run = true;
                else
                    run = false;
            }
            else
                run = Directory.Exists(dirPath);

            string resMessage;
            if (run) // Exists
                resMessage = $"Directory [{rawFilePath}] exists";
            else
                resMessage = $"Directory [{rawFilePath}] does not exists";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            return logs.ToArray();
        }

        /// <summary>
        /// If,ExistSection,<IniFile>,<Section>,<Command>
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistSection(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 2 for condition and 1 for command
            const int necessaryOperandNum = 2;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string iniFile = EscapeString(variables.Expand(subCmd.Operands[0]));
            string section = EscapeString(variables.Expand(subCmd.Operands[1]));
            string rawIniFile = subCmd.Operands[0];
            string rawSection = subCmd.Operands[1];

            string resMessage;
            bool run = IniFile.CheckSectionExist(iniFile, section);
            if (run) // Exists
                resMessage = $"Section [{rawSection}] exists in [{rawIniFile}]";
            else
                resMessage = $"Section [{rawSection}] does not exists in [{rawIniFile}]";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            return logs.ToArray();
        }

        /// <summary>
        /// If,ExistRegSection,<RegRootKey>,<SubKey>,<Command>
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistRegSection(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 2 for condition and 1 for command
            const int necessaryOperandNum = 2;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string rootKey = EscapeString(variables.Expand(subCmd.Operands[0]));
            string subKey = EscapeString(variables.Expand(subCmd.Operands[1]));
            string rawRootKey = subCmd.Operands[0];
            string rawSubKey = subCmd.Operands[1];

            RegistryKey regRoot = RegistryHelper.ParseRootKeyToRegKey(rootKey);
            if (regRoot == null)
                throw new InvalidOperandException($"Invalid registry root key [{rawRootKey}]", cmd);
            RegistryKey regSubKey = regRoot.OpenSubKey(subKey);

            string resMessage;
            bool run = (regSubKey != null);
            if (run) // Exists
            {
                resMessage = $"Registry sub key [{rawRootKey}\\{rawSubKey}] exists";
                regSubKey.Close();
            }
            else
                resMessage = $"Registry sub key [{rawRootKey}\\{rawSubKey}] does not exists";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            regRoot.Close();
            return logs.ToArray();
        }

        /// <summary>
        /// If,ExistRegKey,<RegRootKey>,<SubKey>,<ValueName>,<Command>
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistRegKey(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 3 for condition and 1 for command
            const int necessaryOperandNum = 3;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string rootKey = EscapeString(variables.Expand(subCmd.Operands[0]));
            string subKey = EscapeString(variables.Expand(subCmd.Operands[1]));
            string valueName = EscapeString(variables.Expand(subCmd.Operands[2]));
            string rawRootKey = subCmd.Operands[0];
            string rawSubKey = subCmd.Operands[1];
            string rawValueName = subCmd.Operands[2];

            RegistryKey regRoot = RegistryHelper.ParseRootKeyToRegKey(rootKey);
            if (regRoot == null)
                throw new InvalidOperandException($"Invalid registry root key [{rawRootKey}]", cmd);
            object value = regRoot.OpenSubKey(subKey).GetValue(valueName);

            string resMessage;
            bool run = (value != null);
            if (run) // Exists
            {
                resMessage = $"Registry value [{rootKey}\\{subKey}\\{valueName}] exists";
                regRoot.Close();
            }
            else
                resMessage = $"Registry value [{rootKey}\\{subKey}\\{valueName}] does not exists";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            regRoot.Close();
            return logs.ToArray();
        }

        /// <summary>
        /// If,ExistVar,<%Variable%>,<Command>
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfExistVar(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 1 for condition and 1 for command
            const int necessaryOperandNum = 1;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string varName = BakeryVariables.TrimPercentMark(subCmd.Operands[0]);

            string resMessage;
            bool run = variables.ContainsKey(varName);
            if (run) // Exists
                resMessage = $"Varaible [%{varName}%] exists";
            else
                resMessage = $"Varaible [%{varName}%] does not exists";

            if ((run && !subCmd.NotFlag) || (!run && subCmd.NotFlag))
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            return logs.ToArray();
        }

        /// <summary>
        /// If,<%Variable%>,Equal,<Value><Command>
        /// </summary>
        /// <remarks>
        /// Equal can be substituded by Smaller, Bigger, SmallerEqual, BiggerEqual
        /// ==, <, >, <=, =>, != also supported
        /// </remarks>
        /// <param name="cmd"></param>
        /// <param name="subCmd"></param>
        /// <returns></returns>
        public LogInfo[] IfCompare(BakeryCommand cmd, BakerySubCommand subCmd)
        {
            List<LogInfo> logs = new List<LogInfo>();

            // Necessary operand : 1 for condition and 1 for command
            const int necessaryOperandNum = 2;
            if (cmd.Operands.Length < necessaryOperandNum + 1)
                throw new InvalidOperandException("Necessary operands does not exist", cmd);

            string value1 = EscapeString(variables.Expand(subCmd.Operands[0]));
            string value2 = EscapeString(variables.Expand(subCmd.Operands[1]));

            string resMessage;
            CompareStringNumberResult comp = NumberHelper.CompareStringNumber(value1, value2);

            bool run = false;
            bool notEqual = ((comp & CompareStringNumberResult.NotEqual) == CompareStringNumberResult.NotEqual);
            if ((comp & CompareStringNumberResult.Equal) != 0)
            {
                if ((IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Equal
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.SmallerEqual
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.BiggerEqual)
                    run = true;
                resMessage = $"[{value1}] is equal to [{value2}]";
            }
            else if ((comp & CompareStringNumberResult.Smaller) != 0)
            {
                if ((IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Smaller
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.SmallerEqual
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Bigger && subCmd.NotFlag
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.BiggerEqual && subCmd.NotFlag)
                    run = true;
                resMessage = $"[{value1}] is smaller than [{value2}]";
            }
            else if ((comp & CompareStringNumberResult.Bigger) != 0)
            {
                if ((IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Bigger
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.BiggerEqual
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Smaller && subCmd.NotFlag
                    || (IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.SmallerEqual && subCmd.NotFlag)
                    run = true;
                resMessage = $"[{value1}] is bigger than [{value2}]";
            }
            else if ((comp & CompareStringNumberResult.NotEqual) != 0)
            {
                if ((IfConditionSubOpcode)subCmd.SubOpcode == IfConditionSubOpcode.Equal && subCmd.NotFlag)
                    run = true;
                resMessage = $"[{value1}] is not equal to [{value2}]";
            }
            else
                throw new InternalUnknownException($"Cannot compare [{value1}] and [{value2}]");
            
            if (run)
            {
                logs.Add(new LogInfo(cmd, subCmd, LogState.Success, resMessage));
                BakeryCommand ifCmd = IfInternalCommand(cmd, subCmd, necessaryOperandNum);
                logs.AddRange(ExecuteCommand(ifCmd));
            }
            else
                logs.Add(new LogInfo(cmd, subCmd, LogState.Ignore, resMessage));

            return logs.ToArray();
        }
    }
}