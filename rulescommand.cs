using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using IronPython;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using commands;


namespace rules
{
    public class RulesCommand : ICommand
    {
        private ScriptEngine engine;
        private ScriptScope scope;

        private JObject o;
        string features_file_path;
        string script_file_path;

        public bool Execute()
        {
            Console.WriteLine("Execute Rules");
            Console.WriteLine("Enter path to features file: ");
            this.features_file_path = Console.ReadLine();

            Console.WriteLine("Enter path to script file: ");
            this.script_file_path = Console.ReadLine();

            Execute("");
            return true;
        }

        public bool Execute(string args)
        {
            JObject features;
            JObject script;

            try
            {
                features = JObject.Parse(File.ReadAllText(features_file_path));
                script = JObject.Parse(File.ReadAllText(script_file_path));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: One of the files cannot be found.  Please try again.");
                return Execute();
            }

            engine = Python.CreateEngine();
            scope = engine.CreateScope();

            ICollection<string> paths = engine.GetSearchPaths();
            paths.Add("C:\\Program Files (x86)\\Microsoft Visual Studio\\Shared\\Python36_64\\lib");
            paths.Add("..\\..\\lib");
            engine.SetSearchPaths(paths);
            string[] files = engine.GetModuleFilenames();
            //engine.Execute("import json", scope);
            scope.ImportModule("json");

            o = new JObject();
            o.Add(new JProperty("quotenum", "12345"));
            o.Add("rule_set", script.SelectToken("$.name"));
            o.Add("results", new JArray());
            scope.SetVariable("result_json", o.ToString(Formatting.None));

            ExecuteScript(LoadFeatures(features_file_path), LoadScript(script_file_path));
            Console.WriteLine(string.Format("Script Results: {0}", o.ToString(Formatting.None)));
            File.WriteAllText(@"c:\\users\\johnpinto\\desktop\\rules_results.json", o.ToString(Formatting.Indented));
            return true;
        }

        public bool ExecuteScript(string dataFeatures, string rulesScript)
        {
            try
            {

                JObject features = JObject.Parse(dataFeatures);
                JObject script = JObject.Parse(rulesScript);
                string root = script.SelectToken("$.root").ToString();
                IEnumerable<JToken> variables = script.SelectTokens("$.rules.variables[*]");

                //load script variables
                foreach (var variable in variables)
                {
                    Console.WriteLine("variable name is {0}", variable.ToString());
                    //var match = features.SelectToken(string.Format("$.quote_features.{0}", variable.ToString()));
                    var match = features.SelectToken(string.Format("{0}.{1}", root,variable.ToString()));
                    if (match != null)
                    {
                        Console.WriteLine("feature name is: {0}, value is: {1}", variable.ToString(), match.ToString());
                        scope.SetVariable(variable.ToString(), match);
                    }
                    else
                    {
                        scope.SetVariable(variable.ToString(), 0);
                    }

                }

                // Handle Lookups
                IEnumerable<JToken> lookups = script.SelectTokens("$.rules.lookups[*]");
                foreach (var lookup in lookups)
                {
                    string tmpLkpName = lookup.SelectToken("$.lookup_name").ToString();
                    Console.WriteLine("lookup name is {0}", tmpLkpName);
                    LookupCommand cmd = new LookupCommand();
                    string tmpPath = "C:\\Users\\JohnPinto\\source\\local_projects\\Rating-and-Rules\\New Rating Engine\\";
                    tmpPath += tmpLkpName + ".json";
                    //string strLookup = cmd.Lookup(tmpPath, JsonConvert.SerializeObject(features));
                    //Console.WriteLine("lookup results: {0}",strLookup);
                    //Load Lookup Json...
                    JObject lkp_script = cmd.LoadLookup(tmpPath);

                    //Add filter values from features to filter lookup script.
                    if (mapLookupKeys((JObject)lookup, features, ref lkp_script))
                    {
                        if (cmd.Execute(lkp_script))
                        {
                            JObject lkp_results = cmd.lookup_results_jo;
                            IEnumerable<JToken> results = lkp_results.SelectTokens("$.results[*]");
                            JArray _r = (JArray)lkp_results["results"];
                            loadLookupVariables(lkp_results, scope);

                        }
                    }
                }


                IEnumerable<string> vars = scope.GetVariableNames();
                foreach (var i in vars)
                {
                    var variable_name = scope.GetVariable(i);
                    try
                    {
                        Console.WriteLine("scope variable name is: {0}, value is: {1}", i.ToString(), variable_name != null ? variable_name.ToString() : "none");
                    }
                    catch (Exception ex)
                    {
                        continue;
                    }
                    
                }

                //Execute Script Steps
                ScriptSource src;
                IEnumerable<JToken> steps = script.SelectTokens("$.rules.steps[*]");
                string pyScript = "";
                string result_name = "";
                foreach (var step in steps)
                {
                    string step_name = step.SelectToken("$.name").ToString();
                    string step_type = step.SelectToken("$.type").ToString();
                    result_name = step.SelectToken("$.result_variable").ToString();
                    Console.WriteLine(string.Format("Script step is {0}", step_name));
                    Console.WriteLine(string.Format("script: {0}", step.SelectToken("$.script")));
                    if (step_type.ToLower().Contains("loop"))
                    {
                        var collection = scope.GetVariable(step.SelectToken("$.feature").ToString());
                        foreach (var val in collection)
                        {
                            var result_var = ExecuteCode(step, step.SelectToken("$.script").ToString());
                        }
                    }
                    else if (step_type.ToLower().Contains("script"))
                    {
                        string path = ExtractPythonScript(step.SelectToken("$.script").ToString());
                        path = NormalizeScriptName(path);
                        ExecuteScript(dataFeatures, LoadScript(path));
                    }
                    else if (step_type.ToLower().Contains("code"))
                    {
                        ExecuteCode(step, step.SelectToken("$.script").ToString());
                    }
                    else if (step_type.ToLower().Contains("ifttt"))
                    {
                        ExecuteIFTTT(step, dataFeatures);
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("script error: {0}", ex.Message));
            }
            //JObject 
            return false;

        }

        public string ExecuteCode(JToken step, string pyScript)
        {
            string time = "";
            try
            {
                ScriptSource src;

                pyScript = ExtractPythonScript(pyScript);
                pyScript = NormalizeScript(pyScript);
                src = engine.CreateScriptSourceFromString(pyScript);
                Console.WriteLine(src.GetCode());


                var watch = System.Diagnostics.Stopwatch.StartNew();
                var ret_val = src.Execute(scope);
                watch.Stop();
                decimal elapsedMs = (decimal)(Convert.ToDouble(watch.ElapsedMilliseconds)/Convert.ToDouble(1000));
                time = elapsedMs.ToString();
                string result_name = step.SelectToken("$.result_variable").ToString();
                var result_var = scope.GetVariable(result_name);
                Console.WriteLine(string.Format("result variable is: {0}", result_var.ToString()));

                string step_name = step.SelectToken("$.name").ToString();
                JObject log = new JObject(new JProperty("step", step_name), new JProperty(result_name, result_var.ToString()), new JProperty("time", time));
                JArray entry = (JArray)o["results"];
                entry.Add(log);

                return result_var.ToString();
            }
            catch (Exception ex)
            {
                string step_name = step.SelectToken("$.name").ToString();
                string error_message = string.Format("step {0}, code: {1},  failed with error {2}", step_name, pyScript, ex.Message);
                JObject log = new JObject(new JProperty("step", step_name), new JProperty("result", error_message), new JProperty("time", time));
                Console.WriteLine(error_message);
                throw;
            }
        }

        public bool ExecuteIFTTT (JToken step, string dataFeatures)
        {
            string time = "";
            JArray scripts = (JArray)step.SelectToken("$.script");
            try
            {
                
                foreach (var script in scripts)
                {
                    
                    var result_variable = script.SelectToken("$.if_variable");
                    var result_var = scope.GetVariable(result_variable.ToString());
                    if(result_var == "True")
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        ExecuteScript(dataFeatures, LoadScript(script.SelectToken("$.is_true").ToString()));
                        watch.Stop();
                        decimal elapsedMs = (decimal)(Convert.ToDouble(watch.ElapsedMilliseconds)/Convert.ToDouble(1000));
                        time = elapsedMs.ToString();
                    }
                    else if(result_var == "False")
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        ExecuteScript (dataFeatures, LoadScript(script.SelectToken("$.is_false").ToString()));
                        watch.Stop();
                        decimal elapsedMs = (decimal)(Convert.ToDouble(watch.ElapsedMilliseconds)/Convert.ToDouble(1000));
                        time = elapsedMs.ToString();
                    }
                    else
                    {
                        time = "0";
                        throw new Exception (string.Format("ExecuteIFTTT error: result variable {0} was not True/False {1}", result_variable.ToString(), result_var.ToString()));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                string step_name = step.SelectToken("$.name").ToString();
                string error_message = string.Format("step {0}, code: {1},  failed with error {2}", step_name, scripts.ToString(), ex.Message);
                JObject log = new JObject(new JProperty("step", step_name), new JProperty("result", error_message), new JProperty("time", time));
                Console.WriteLine(error_message);
                throw;

            }
        }

        public string LoadFeatures(string location)
        {
            return File.ReadAllText(location);
        }

        public string LoadScript(string location)
        {
            return File.ReadAllText(location);
        }

        public string Name() { return "Rules Command"; }

        private string NormalizeScript(string input)
        {

            //input = input.Replace("[","");
            //input = input.Replace("]","");
            input = input.Replace("\"", "");
            input = input.Replace(",", "");
            string tmp = input.Replace("\r\n  ", "\n");
            Console.WriteLine(string.Format("normalized string: {0}", tmp));

            return tmp;
        }

        private string NormalizeScriptName (string input)
        {
            input = NormalizeScript(input);
            input = input.Replace("\n", "");
            return input;
        }

        private string ExtractPythonScript(string input)
        {
            string left_bracket = "[";
            string right_bracket = "]";
            int place = input.IndexOf(left_bracket);
            input = input.Remove(place, left_bracket.Length).Insert(place, "");

            place = input.LastIndexOf(right_bracket);
            input = input.Remove(place, right_bracket.Length).Insert(place, "");

            input = input.Replace(",", "");

            string tmp = input.Replace("\r\n  ", "\n");
            Console.WriteLine(string.Format("extracted python script: {0}", tmp));
            return tmp;

        }

        private string NormalizePythonScript(string src)
        {
            var rx = new Regex("\\\\\"([0-9A-Fa-f]+)");
            var res = new StringBuilder();
            var pos = 0;
            foreach (Match m in rx.Matches(src))
            {
                res.Append(src.Substring(pos, m.Index - pos));
                pos = m.Index + m.Length;
                res.Append((char)Convert.ToInt32(m.Groups[1].ToString(), 16));
            }
            res.Append(src.Substring(pos));
            return res.ToString();
        }

        private bool loadLookupVariables(JObject results, ScriptScope scope)
        {
            IEnumerable<JToken> res = results.SelectTokens("$.results[*]");
            foreach (JProperty o in res.Children())
            {
                if (o.HasValues)
                {
                    Console.WriteLine("name: {0}", o.Name);
                    Console.WriteLine("value: {0}", o.Value);
                    scope.SetVariable(o.Name, o.Value);
                }
            }

            return false;
        }

        //Use the lookup_map to map features to filter values in the lookup_script
        private bool mapLookupKeys(JObject lookup_map, JObject features, ref JObject lookup_script)
        {

            IEnumerable<JToken> map_items = lookup_map.SelectTokens("$.lookup_map[*]");
            IEnumerable<JToken> feature_list = features.SelectTokens("$.quote_features");
            IEnumerable<JToken> keys = lookup_script.SelectTokens("$.keys[*]");
            foreach (JObject item in map_items)
            {
                //find the feature that matches the current item in map_items
                string source_name = item.SelectToken("$.source").ToString();
                var match = features.SelectToken(string.Format("$.quote_features.{0}", source_name));

                //if found, get the value from match, and update the "column" key in lookup script.
                if (match != null)
                {
                    foreach (var key in keys)
                    {
                        string key_column = key.SelectToken("$.column").ToString();
                        string map_column = item.SelectToken("$.column").ToString();
                        if (key_column == map_column)
                        {
                            Console.WriteLine("updating {0} with value {1}", key.ToString(), match);
                            var newToken = new JProperty("filter_value", (match));
                            key["filter_value"] = match;
                            //Console.WriteLine("filter_value: {0}, value: {1}",k.ToString(),k);
                            //JProperty k_val = k.Value<JProperty>("filter_value");
                            //k_val.Value = match[source_name];
                            Console.WriteLine("key: {0} matches map: {1}", key_column, map_column);
                            break;
                        }
                    }
                }
            }

            return true;

        }
    }
}