using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using commands;


namespace rules
{
    public class LookupCommand:ICommand 
    {
        private string lookup_script_path;
        //private string _lookup_result;
        public string lookup_results {get;set;}

        protected JObject script;

        public JObject lookup_results_jo {get; set;}
        
        public LookupCommand(){

        }

        public bool Execute ()
        {
            Console.WriteLine("Execute Lookup");
            Console.WriteLine("Enter path to lookup file: ");
            this.lookup_script_path = Console.ReadLine();

            return Execute("");
        }

        public bool Execute(string args)
        {
            try
            {
                script = JObject.Parse(File.ReadAllText(lookup_script_path));
                return ExecuteLookup();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: One of the files cannot be found.  Please try again.");
                return Execute();
            }
            
            
        }

        public bool Execute (JObject lookup)
        {
            script = lookup;
            return ExecuteLookup();
        }

        public string Lookup(string lookup_name, string features=null)
        {
            //for now lookup_name = path to file.json
            lookup_script_path = lookup_name;
            Execute (lookup_name);
            return lookup_results;
        }

        public JObject LoadLookup(string lookup_name)
        {
            return JObject.Parse(File.ReadAllText(lookup_name));
        }

        public string Name(){ return "LookupCommand"; }

        protected bool ExecuteLookup()
        {
            string table = (string)script.SelectToken("$.table");
            Console.WriteLine("table name is {0}", table);

            IEnumerable<JToken> target_columns = script.SelectTokens("$.target_columns[*]");
            string target_column_query = createTargetColumnSearch(target_columns);

            IEnumerable<JToken> keys = script.SelectTokens("$.keys[*]");
            string filter_query = createQueryFilter(keys);
            
            string query = createQuery(table, target_column_query, filter_query);
            
            SqlConnection conn = new SqlConnection();
            conn.ConnectionString = "";
            conn.Open();

 //           string[] columnRestrictions = new string[4];
 //           columnRestrictions[2] = table;
 //           foreach(var col in target_columns)
 //           {
 //               columnRestrictions[3] = col.ToString();
 //               DataTable metadata = conn.GetSchema("Columns", columnRestrictions);
 //               ShowColumns(metadata);
 //           }

            SqlCommand cmd = new SqlCommand();        
            cmd.CommandText = query;
            cmd.CommandType = CommandType.Text;
            cmd.Connection = conn;

            SqlDataReader rdr = cmd.ExecuteReader();
            DataTable t = new DataTable("results");
            t.Load(rdr);
            DataSet set = new DataSet();
            set.Tables.Add(t);
            
            if(t.Rows.Count <= 0)
            {
                return false;
            }

            JObject jo = JObject.FromObject(set);
            lookup_results_jo = new JObject(new JProperty("results"));

            IEnumerable<JToken> results = script.SelectTokens("$.results[*]");
            JArray _r = (JArray)lookup_results_jo["results"];
            foreach(JToken res in results )
            {
                string source = res.SelectToken("$.source").ToString();
                string target = res.SelectToken("$.target").ToString();
                Console.WriteLine("map from: {0} to: {1}",source ,target);
                
                _r.Add(new JObject(mapResults(source, target, jo)));
            }

            lookup_results = JsonConvert.SerializeObject(lookup_results_jo, Formatting.Indented);
            Console.WriteLine("results are: {0}",lookup_results);

            return true;
        }
        
       

        private string createTargetColumnSearch(IEnumerable<JToken> target_columns)
        {
            List<string> cols = new List<string>();

            foreach(var column in target_columns)
            {
                cols.Add(column.ToString());
            }

            int len = cols.Count;
            int i = 0;

            string retval = "";
            foreach (var col in cols)
            {
                i++;
                if(i == len)
                {
                    retval += col;
                }
                else
                {
                    retval += col + ", ";
                }
            }
            return retval;
        }

        private string createQueryFilter(IEnumerable<JToken> keys)
        {
            string filter_sql = "";

            List<string> filters = new List<string>();
            
            foreach(var key in keys)
            {
                string col = key.SelectToken("$.column").ToString();
                var filter = key.SelectToken("$.filter");
                var filter_value = key.SelectToken("$.filter_value");
                Console.WriteLine("column name is {0}", col);
                Console.WriteLine("filter is {0}", filter.ToString()); 
                Console.WriteLine("filter_value is {0}",filter_value.ToString());
                string statement = string.Format("{0} {1} '{2}'",col, filter, filter_value );
                filters.Add(statement);
            }

            int len = filters.Count;
            int i = 0;
            foreach(var filter in filters )
            {
                i++;
                if(i == len)
                {
                    filter_sql += filter;
                }
                else 
                {
                    filter_sql += (filter + " AND ");
                }
            }

            Console.WriteLine ("filter_sql is {0}", filter_sql);
            return filter_sql;

        }

        private string createQuery(string table_name, string target_column_query, string filter_query)
        {
            string base_query = string.Format("SELECT TOP 1 {0} FROM {1} WHERE {2}", target_column_query,  table_name, filter_query);
            Console.WriteLine("base_query is : {0}", base_query);
            return base_query;
        }

        private static void ShowDataTable(DataTable table, Int32 length) 
        {
            foreach (DataColumn col in table.Columns) {
                Console.Write("{0,-" + length + "}", col.ColumnName);
            }
            Console.WriteLine();

            foreach (DataRow row in table.Rows) {
                foreach (DataColumn col in table.Columns) {
                    if (col.DataType.Equals(typeof(DateTime)))
                    Console.Write("{0,-" + length + ":d}", row[col]);
                    else if (col.DataType.Equals(typeof(Decimal)))
                    Console.Write("{0,-" + length + ":C}", row[col]);
                    else
                    Console.Write("{0,-" + length + "}", row[col]);
                }
                Console.WriteLine();
            }
        }

         private static void ShowColumns(DataTable columnsTable) 
         {
             var selectedRows = from info in columnsTable.AsEnumerable()
                         select new {
                            TableCatalog = info["TABLE_CATALOG"],
                            TableSchema = info["TABLE_SCHEMA"],
                            TableName = info["TABLE_NAME"],
                            ColumnName = info["COLUMN_NAME"],
                            DataType = info["DATA_TYPE"]
                         };

            Console.WriteLine("{0,-15}{1,-15}{2,-15}{3,-15}{4,-15}", "TableCatalog", "TABLE_SCHEMA",
                "TABLE_NAME", "COLUMN_NAME", "DATA_TYPE");
            foreach (var row in selectedRows) {
                Console.WriteLine("{0,-15}{1,-15}{2,-15}{3,-15}{4,-15}", row.TableCatalog,
                    row.TableSchema, row.TableName, row.ColumnName, row.DataType);
            }
            
        }

        private JToken mapResults(string source, string target, JObject results)
        {
           IEnumerable<JToken> res = results.SelectTokens("$.results[*]");
           foreach(JProperty o in res.Children())
           {
               if(o.HasValues)
               {  
                    Console.WriteLine("name: {0}",o.Name);
                    Console.WriteLine("value: {0}",o.Value);
                    if(o.Name == source)
                    {
                        return new JProperty(target, o.Value);
                    }
               }
           }
           
           return null;
        }
    }

    






}