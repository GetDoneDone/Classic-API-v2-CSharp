using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Web;
using System.Security.Cryptography;
using System.IO;

namespace DoneDone.APIV2WrapperCSharp
{
    public sealed class APIException : Exception
    {
        private HttpWebResponse _response;

        public HttpWebResponse Response
        {
            get
            {
                return _response;
            }
        }

        public APIException(string message, HttpWebResponse resp) : base(message)
        {
            _response = resp;
        }
    }

    /// <summary>
    /// Provide access to the DoneDone IssueTracker API. 
    /// </summary>
    public class IssueTracker
    {
        protected string baseURL;
        protected string auth;

        private enum RequestMethods
        {
            GET, POST, PUT, DELETE
        }

        #region Public constructor methods

        /// <summary>
        /// Public default constructor
        /// </summary>
        /// <param name="subdomain">Subdomain of DoneDone account (e.g. mycompany.mydonedone.com -> subdomain = mycompany)</param>
        /// <param name="username">DoneDone username</param>
        /// <param name="passwordOrAPIToken">DoneDone password or API Token</param>
        public IssueTracker(string subdomain, string username, string passwordOrAPIToken)
        {
            auth = Convert.ToBase64String(Encoding.Default.GetBytes(string.Format("{0}:{1}", username, passwordOrAPIToken)));
            baseURL = string.Format("http://{0}.mydonedone.com/issuetracker/api/v2/", subdomain);
        }
         
        #endregion

        #region Private helper methods

        /// <summary>
        /// Get mime type for a file
        /// </summary>
        /// <param name="Filename">file name</param>
        /// <returns></returns>
        private string getMimeType(string Filename)
        {
            string mime = "application/octetstream";
            string ext = Path.GetExtension(Filename).ToLower();
            Microsoft.Win32.RegistryKey rk = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);

            if (rk != null && rk.GetValue("Content Type") != null)
            {
                mime = rk.GetValue("Content Type").ToString();
            }
            
            return mime;
        }

        /// <summary>
        /// Perform generic API calling
        /// </summary>
        /// <param name="methodURL">IssueTracker method URL</param>
        /// <param name="data">Generic data</param>
        /// <param name="attachments">List of file paths (optional)</param>
        /// <param name="update">flag to indicate if this is a  PUT operation</param>
        /// <returns>the JSON string returned from server</returns>
        private string api(string methodURL, RequestMethods request_method,
            List<KeyValuePair<string, string>> data = null, List<string> attachments = null)
        {
            string url = baseURL + methodURL;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Headers.Add("Authorization: Basic " + auth);
            request.Method = request_method.ToString();

            if (data != null || attachments != null)
            {
                byte[] formData = null;

                if (attachments == null)
                {
                    request.ContentType = "application/x-www-form-urlencoded";

                    var postParams = new List<string>();

                    foreach (KeyValuePair<string, string> item in data)
                    {
                        postParams.Add(String.Format("{0}={1}", item.Key, Uri.EscapeUriString(item.Value)));
                    }

                    string postQuery = String.Join("&", postParams.ToArray());
                    formData = Encoding.UTF8.GetBytes(postQuery);
                    request.ContentLength = formData.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(formData, 0, formData.Length);
                        requestStream.Flush();
                    }
                }
                else
                {
                    var boundary = "------------------------" + DateTime.Now.Ticks;
                    var newLine = Environment.NewLine;

                    request.ContentType = "multipart/form-data; boundary=" + boundary;

                    using (var requestStream = request.GetRequestStream())
                    {
                        #region Stream data to request

                        var fieldTemplate = newLine + "--" + boundary + newLine + "Content-Type: text/plain" + 
                            newLine + "Content-Disposition: form-data;name=\"{0}\"" + newLine + newLine + "{1}";

                        var fieldData = "";

                        foreach (KeyValuePair<string, string> item in data)
                        {
                            fieldData += String.Format(fieldTemplate, item.Key, item.Value);
                        }

                        var fieldBytes = Encoding.UTF8.GetBytes(fieldData);
                        requestStream.Write(fieldBytes, 0, fieldBytes.Length);

                        #endregion

                        #region Stream files to request

                        var fileInfoTemplate = newLine + "--" + boundary + newLine + "Content-Disposition: filename=\"{0}\"" + 
                            newLine + "Content-Type: {1}" + newLine + newLine;

                        foreach (var path in attachments)
                        {
                            using (var reader = new BinaryReader(File.OpenRead(path)))
                            {
                                #region Stream file info

                                var fileName = Path.GetFileName(path);
                                var fileInfoData = String.Format(fileInfoTemplate, fileName, getMimeType(fileName));
                                var fileInfoBytes = Encoding.UTF8.GetBytes(fileInfoData);

                                requestStream.Write(fileInfoBytes, 0, fileInfoBytes.Length);

                                #endregion 

                                #region Stream file

                                using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read))
                                {
                                    byte[] buffer = new byte[4096];
                                    var fileBytesRead = 0;

                                    while ((fileBytesRead = fileStream.Read(buffer, 0, buffer.Length)) != 0)
                                    {
                                        requestStream.Write(buffer, 0, fileBytesRead);
                                    }
                                }

                                #endregion
                            }
                        }

                        var trailer = Encoding.ASCII.GetBytes(newLine + "--" + boundary + "--");
                        requestStream.Write(trailer, 0, trailer.Length);

                        #endregion
                    }
                }
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(responseStream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                var response = (HttpWebResponse)wex.Response;

                string message = "An API error occurred.";

                if (response == null)
                {
                    throw new APIException(message, null);
                }

                var code = response.StatusCode;

                using (Stream responseStream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(responseStream))
                    {
                        message = reader.ReadToEnd();
                    }
                }

                throw new APIException(message, response);
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        private string getIssuesByDefaultFilterForProject(string url,
            List<long> tag_ids = null,
            DateTime? start_due_date = null,
            DateTime? end_due_date = null,
            short? sort = null,
            short? issue_creation_type = null,
            int? skip = null,
            int? take = null)
        {
            url += "?";

            if (tag_ids != null)
            {
                url += string.Format("tag_ids={0}&", tag_ids);
            }

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (sort.HasValue)
            {
                url += string.Format("sort={0}&", sort.Value.ToString());
            }

            if (issue_creation_type.HasValue)
            {
                url += string.Format("issue_creation_type={0}&", issue_creation_type.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        private string getIssuesByDefaultFilter(string url,
          List<int> project_ids = null,
          List<long> tag_ids = null,
          DateTime? start_due_date = null,
          DateTime? end_due_date = null,
          short? sort = null,
          short? issue_creation_type = null,
          int? skip = null,
          int? take = null)
        {
            url += "?";

            if (project_ids != null)
            {
                url += string.Format("project_ids={0}&", String.Join(",", project_ids));
            }

            if (tag_ids != null)
            {
                url += string.Format("tag_ids={0}&", String.Join(",", tag_ids));
            }

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (sort.HasValue)
            {
                url += string.Format("sort={0}&", sort.Value.ToString());
            }

            if (issue_creation_type.HasValue)
            {
                url += string.Format("issue_creation_type={0}&", issue_creation_type.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        private string getActivityByDefaultIssueFilter(string url,
            List<int> project_ids = null,
            List<long> tag_ids = null,
            DateTime? start_due_date = null,
            DateTime? end_due_date = null,
            DateTime? from_date = null,
            DateTime? until_date = null,
            double hours_from_utc = 0,
            short? issue_creation_type = null,
            int? skip = null,
            int? take = null)
        {
            url += "?";

            if (project_ids != null)
            {
                url += string.Format("project_ids={0}&", String.Join(",", project_ids));
            }

            if (tag_ids != null)
            {
                url += string.Format("tag_ids={0}&", String.Join(",", tag_ids));
            }

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (from_date.HasValue)
            {
                url += string.Format("from_date={0}&", from_date.Value.ToShortDateString());
            }

            if (until_date.HasValue)
            {
                url += string.Format("until_date={0}&", until_date.Value.ToShortDateString());
            }

            url += string.Format("hours_from_utc={0}&", hours_from_utc);

            if (issue_creation_type.HasValue)
            {
                url += string.Format("issue_creation_type={0}&", issue_creation_type.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        private string getActivityByDefaultIssueFilterForProject(string url,
            List<long> tag_ids = null,
            DateTime? start_due_date = null,
            DateTime? end_due_date = null,
            DateTime? from_date = null,
            DateTime? until_date = null,
            double hours_from_utc = 0,
            short? issue_creation_type = null,
            int? skip = null,
            int? take = null)
        {
            url += "?";

            if (tag_ids != null)
            {
                url += string.Format("tag_ids={0}&", String.Join(",", tag_ids));
            }

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (from_date.HasValue)
            {
                url += string.Format("from_date={0}&", from_date.Value.ToShortDateString());
            }

            if (until_date.HasValue)
            {
                url += string.Format("until_date={0}&", until_date.Value.ToShortDateString());
            }

            url += string.Format("hours_from_utc={0}&", hours_from_utc);
           
            if (issue_creation_type.HasValue)
            {
                url += string.Format("issue_creation_type={0}&", issue_creation_type.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        #endregion

        #region Public API wrapper methods

        public string GetCompanies()
        {
            string url = "companies.json";
            return api(url, RequestMethods.GET);
        }

        public string CreateCompany(string company_name)
        {
            string url = "companies.json";

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("company_name", company_name));

            return api(url, RequestMethods.POST, data);
        }

        public string UpdateCompany(int company_id, string company_name)
        {
            string url = string.Format("companies/{0}.json", company_id);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("company_name", company_name));

            return api(url, RequestMethods.PUT, data);
        }

        public string GetCompany(int company_id)
        {
            string url = string.Format("companies/{0}.json", company_id);
            return api(url, RequestMethods.GET);
        }

        public string GetPerson(int user_id)
        {
            string url = string.Format("people/{0}.json", user_id);
            return api(url, RequestMethods.GET);
        }

        public string GetProjects()
        {
            string url = "projects.json";
            return api(url, RequestMethods.GET);
        }

        public string GetPeopleInProject(int project_id)
        {
            string url =  string.Format("projects/{0}/people.json", project_id);
            return api(url, RequestMethods.GET);
        }


        public string GetProject(int project_id)
        {
            string url = string.Format("projects/{0}.json", project_id);
            return api(url, RequestMethods.GET);
        }

        public string GetIssue(int project_id, int order_number)
        {
            string url = string.Format("projects/{0}/issues/{1}.json", project_id, order_number);
            return api(url, RequestMethods.GET);
        }

        public string CreateIssue(
            int project_id,
            string title,
            short priority_level_id,
            int fixer_id,
            int tester_id,
            string description = null,
            List<string> tags = null,
            List<int> user_ids_to_cc = null,
            DateTime? due_date = null,
            List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues.json", project_id);

            var data = new List<KeyValuePair<string, string>>();

            data.Add(new KeyValuePair<string, string>("title", title));
            data.Add(new KeyValuePair<string, string>("priority_level_id", priority_level_id.ToString()));
            data.Add(new KeyValuePair<string, string>("fixer_id", fixer_id.ToString()));
            data.Add(new KeyValuePair<string, string>("tester_id", tester_id.ToString()));

            if (description != null)
            {
                data.Add(new KeyValuePair<string, string>("description", description));
            }

            if (tags != null)
            {
                data.Add(new KeyValuePair<string, string>("tags", String.Join(",", tags)));
            }

            if (user_ids_to_cc != null)
            {
                data.Add(new KeyValuePair<string, string>("user_ids_to_cc", String.Join(",", user_ids_to_cc)));
            }

            if (due_date != null)
            {
                data.Add(new KeyValuePair<string, string>("due_date", due_date.ToString()));
            }

            return api(url, RequestMethods.POST, data, attachments);
        }

        public string GetPeopleAvailableForReassignment(int project_id, int order_number)
        {
            string url = string.Format("projects/{0}/issues/{1}/people/available_for_reassignment.json", project_id, order_number);
            return api(url, RequestMethods.GET);
        }

        public string GetStatusesAvailableToChangeTo(int project_id, int order_number)
        {
            string url = string.Format("projects/{0}/issues/{1}/statuses/available_to_change_to.json", project_id, order_number);
            return api(url, RequestMethods.GET);
        }

        public string AddCommentToIssue(int project_id, int order_number, string comment, List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues/{1}/comments.json", project_id, order_number);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("comment", comment));

            return api(url, RequestMethods.POST, data, attachments);
        }

        public string UpdateStatusOfIssue(int project_id, int order_number, string comment, short new_status_id, List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues/{1}/status.json", project_id, order_number);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("comment", comment));
            data.Add(new KeyValuePair<string, string>("new_status_id", new_status_id.ToString()));

            return api(url, RequestMethods.PUT, data, attachments);
        }

        public string UpdateFixerOfIssue(int project_id, int order_number, string comment, int new_fixer_id, List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues/{1}/fixer.json", project_id, order_number);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("comment", comment));
            data.Add(new KeyValuePair<string, string>("new_fixer_id", new_fixer_id.ToString()));

            return api(url, RequestMethods.PUT, data, attachments);
        }

        public string UpdateTesterOfIssue(int project_id, int order_number, string comment, int new_tester_id, List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues/{1}/tester.json", project_id, order_number);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("comment", comment));
            data.Add(new KeyValuePair<string, string>("new_tester_id", new_tester_id.ToString()));

            return api(url, RequestMethods.PUT, data, attachments);
        }

        public string UpdatePriorityLevelOfIssue(int project_id, int order_number, string comment, int new_priority_level_id, List<string> attachments = null)
        {
            string url = string.Format("projects/{0}/issues/{1}/priority_level.json", project_id, order_number);

            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("comment", comment));
            data.Add(new KeyValuePair<string, string>("new_priority_level_id", new_priority_level_id.ToString()));

            return api(url, RequestMethods.PUT, data, attachments);
        }

        public string GetReleaseBuildsForProject(int project_id)
        {
            string url = string.Format("projects/{0}/release_builds.json", project_id);
            return api(url, RequestMethods.GET);
        }

        public string GetReleaseBuildsInfoForProject(int project_id)
        {
            string url = string.Format("projects/{0}/release_builds/info.json", project_id);
            return api(url, RequestMethods.GET);
        }

        public string CreateReleaseBuildsForProject(int project_id, List<int> order_numbers, string title, string description, string email_body, List<int> user_ids_to_cc)
        {
            string url = string.Format("projects/{0}/release_builds.json", project_id);
            
            var data = new List<KeyValuePair<string, string>>();
            data.Add(new KeyValuePair<string, string>("order_numbers", String.Join(",", order_numbers)));
            data.Add(new KeyValuePair<string, string>("user_ids_to_cc", String.Join(",", user_ids_to_cc)));
            data.Add(new KeyValuePair<string, string>("title", title));
            data.Add(new KeyValuePair<string, string>("email_body", email_body));
            data.Add(new KeyValuePair<string, string>("description", description));

            return api(url, RequestMethods.POST, data);
        }

        public string GetPriorityLevels()
        {
            string url = "priority_levels.json";
            return api(url, RequestMethods.GET);
        }
        
        public string GetIssueCreationTypes()
        {
            string url = "issue_creation_types.json";
            return api(url, RequestMethods.GET);
        }

        public string GetIssueSortTypes()
        {
            string url = "issue_sort_types.json";
            return api(url, RequestMethods.GET);
        }

        public string GetCustomFiltersForProject(int project_id)
        {
            string url = string.Format("projects/{0}/custom_filters.json", project_id);
            return api(url, RequestMethods.GET);
        }

        public string GetGlobalCustomFilters()
        {
            string url = "global_custom_filters.json";
            return api(url, RequestMethods.GET);
        }

        public string DeleteIssue(int project_id, int order_number)
        {
            string url = string.Format("projects/{0}/issues/{1}.json", project_id, order_number);
            return api(url, RequestMethods.DELETE);
        }

        public string GetIssuesWaitingOnYouForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/waiting_on_you.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesWaitingOnThemForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/waiting_on_them.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesYoureCcdOnForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
           short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/youre_ccd_on.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetYourActiveIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
           short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/your_active.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllYourIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/all_yours.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllActiveIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/all_active.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllClosedAndFixedIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/all_closed_and_fixed.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/all.json", project_id);
            return getIssuesByDefaultFilterForProject(url, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesByCustomFilterForProject(int project_id, int custom_filter_id, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/issues/by_custom_filter/{1}.json?", project_id, custom_filter_id);

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (sort.HasValue)
            {
                url += string.Format("sort={0}&", sort.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        public string GetIssuesWaitingOnYou(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/waiting_on_you.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesWaitingOnThem(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/waiting_on_them.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesYoureCcdOn(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
           short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/youre_ccd_on.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetYourActiveIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
           short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/your_active.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllYourIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/all_yours.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllActiveIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/all_active.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllClosedAndFixedIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/all_closed_and_fixed.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetAllIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "issues/all.json";
            return getIssuesByDefaultFilter(url, project_ids, tag_ids, start_due_date, end_due_date, sort, issue_creation_type, skip, take);
        }

        public string GetIssuesByCustomFilter( int custom_filter_id, DateTime? start_due_date = null, DateTime? end_due_date = null,
          short? sort = null, int? skip = null, int? take = null)
        {
            string url = string.Format("issues/by_global_custom_filter/{0}.json?", custom_filter_id);

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (sort.HasValue)
            {
                url += string.Format("sort={0}&", sort.Value.ToString());
            }

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }
        
        public string GetActivityForIssuesWaitingOnYouForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/issues_waiting_on_you.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesWaitingOnThemForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/issues_waiting_on_them.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesYoureCcdOnForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/issues_youre_ccd_on.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForYourActiveIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/your_active_issues.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllYourIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/all_your_issues.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllActiveIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/all_active_issues.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllClosedAndFixedIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/all_closed_and_fixed_issues.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllIssuesForProject(int project_id, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/all_issues.json", project_id);
            return getActivityByDefaultIssueFilterForProject(url, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesByCustomFilterForProject(int project_id, int custom_filter_id, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, int? skip = null, int? take = null)
        {
            string url = string.Format("projects/{0}/activity/issues_by_custom_filter/{1}.json?", project_id, custom_filter_id);

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (from_date.HasValue)
            {
                url += string.Format("from_date={0}&", from_date.Value.ToShortDateString());
            }

            if (until_date.HasValue)
            {
                url += string.Format("until_date={0}&", until_date.Value.ToShortDateString());
            }

            url += string.Format("hours_from_utc={0}&", hours_from_utc);

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        public string GetActivityForIssuesWaitingOnYou(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/issues_waiting_on_you.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesWaitingOnThem(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/issues_waiting_on_them.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesYoureCcdOn(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/issues_youre_ccd_on.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForYourActiveIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/your_active_issues.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllYourIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/all_your_issues.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllActiveIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/all_active_issues.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllClosedAndFixedIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/all_closed_and_fixed_issues.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForAllIssues(List<int> project_ids, List<long> tag_ids = null, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, short? issue_creation_type = null, int? skip = null, int? take = null)
        {
            string url = "activity/all_issues.json";
            return getActivityByDefaultIssueFilter(url, project_ids, tag_ids, start_due_date, end_due_date, from_date, until_date, hours_from_utc, issue_creation_type, skip, take);
        }

        public string GetActivityForIssuesByCustomFilter(int custom_filter_id, DateTime? start_due_date = null, DateTime? end_due_date = null,
            DateTime? from_date = null, DateTime? until_date = null, double hours_from_utc = 0, int? skip = null, int? take = null)
        {
            string url = string.Format("activity/issues_by_global_custom_filter/{0}.json?", custom_filter_id);

            if (start_due_date.HasValue)
            {
                url += string.Format("start_due_date={0}&", start_due_date.Value.ToShortDateString());
            }

            if (end_due_date.HasValue)
            {
                url += string.Format("end_due_date={0}&", end_due_date.Value.ToShortDateString());
            }

            if (from_date.HasValue)
            {
                url += string.Format("from_date={0}&", from_date.Value.ToShortDateString());
            }

            if (until_date.HasValue)
            {
                url += string.Format("until_date={0}&", until_date.Value.ToShortDateString());
            }

            url += string.Format("hours_from_utc={0}&", hours_from_utc);

            if (skip.HasValue)
            {
                url += string.Format("skip={0}&", skip.Value.ToString());
            }

            if (take.HasValue)
            {
                url += string.Format("take={0}&", take.Value.ToString());
            }

            return api(url, RequestMethods.GET);
        }

        #endregion

    }
}
