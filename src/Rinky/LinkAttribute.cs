using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Web.Http;
using System.Text.RegularExpressions;
using System.Web.Http.Filters;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rinky
{
    /// <summary>
    /// Modifies the result to inject a resource link.
    /// </summary>
    public class LinkAttribute : ActionFilterAttribute {
        
        #region ActionFilterAttribute overrides

        /// <summary>
        /// in the Action Executed pipeline, LinkAttribute will inject the 
        /// specified link into any mathching members contained in the 
        /// actionExecutedContext's response content.
        /// </summary>
        /// <param name="actionExecutedContext">The context that will be filtered.</param>
        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext) {

            // if there's been a problem up to this point, LinkAttribute has nothing to do.
            if (actionExecutedContext.Exception == null) {
                try {
                    if (actionExecutedContext.Response.StatusCode == this.statusCode) {
                        object actionResult;
                        actionExecutedContext.Response.TryGetContentValue(out actionResult);
                        if (actionResult != null) {
                            // apply this Link's query recursively
                            // first, seed the recursion with the root object, current object and current property 
                            // all set to the action's result

                            var injectedObject = Newtonsoft.Json.Linq.JToken.FromObject(actionResult, serializer);

                            JToken obj = injectedObject;

                            Walk(obj, this);

                            actionExecutedContext.Response = actionExecutedContext.Request.CreateResponse(actionExecutedContext.Response.StatusCode, injectedObject.ToObject<object>());
                        }
                    }
                } catch {
                    // if LinkAttribute has a problem, it does leaves the original Response in place.
                }
            }
        }

        #endregion

        #region private members

        private string rel;
        private string href;
        private HttpStatusCode statusCode;
        private string[] query;
        JsonSerializer serializer; 

        #endregion

        #region ctors

        /// <summary>
        /// Modifies the result to inject a resource link.
        /// </summary>
        /// <param name="Rel">Identifies the type of of the link</param>
        /// <param name="Href">The relative route to the linked resource. To include a resource value from the linking resource in the injected link, enclose its relative path in {curly braces}. Property names in this relative path are case-sensitive.</param>
        public LinkAttribute(string Rel, string Href) : this(Rel, Href, HttpStatusCode.OK, null) { }

        /// <summary>
        /// Modifies the result to inject a resource link.
        /// </summary>
        /// <param name="Rel">Identifies the type of of the link</param>
        /// <param name="Href">The relative route to the linked resource. To include a resource value from the linking resource in the injected link, enclose its relative path in {curly braces}. Property names in this relative path are case-sensitive.</param>
        /// <param name="Status">Conditions injection of a link on the status code of the response. Default is HttpStatusCode.OK</param>
        public LinkAttribute(string Rel, string Href, HttpStatusCode Status) : this(Rel, Href, Status, null) { }

        /// <summary>
        /// Modifies the result to inject a resource link.
        /// </summary>
        /// <param name="Rel">Identifies the type of of the link</param>
        /// <param name="Href">The relative route to the linked resource. To include a resource value from the linking resource in the injected link, enclose its relative path in {curly braces}. Property names in this relative path are case-sensitive.</param>
        /// <param name="Query">List of property names, used to find subproperties of the linking resource into which to inject links.</param>
        /// <example>An attributed route returns an array of orders, each containing an array of order lines.  To inject a link into each 
        /// order line detail:
        /// <code>
        /// [Link("orderLineDetail", "api/orders/{OrderNo}/detail/{OrderLineNo}", "Results", "[]", "OrderLines", "[]")]
        /// </code>
        /// </example>
        /// <example>The same example as above, but the resource being returned is a newly created order, so we only want links if the creation of the order was successful: 
        /// <code>
        /// [Link("orderLineDetail", "api/orders/{OrderNo}/detail/{OrderLineNo}", HttpStatusCode.Created, "Results", "[]", "OrderLines", "[]")]
        /// </code>
        /// </example>
        public LinkAttribute(string Rel, string Href, params string[] Query) : this(Rel, Href, HttpStatusCode.OK, Query) { }

        /// <summary>
        /// Modifies the result to inject a resource link.
        /// </summary>
        /// <param name="Rel">Identifies the type of of the link</param>
        /// <param name="Href">The relative route to the linked resource. To include a resource value from the linking resource in the injected link, enclose its relative path in {curly braces}. Property names in this relative path are case-sensitive.</param>
        /// <param name="Status">Conditions injection of a link on the status code of the response. Default is HttpStatusCode.OK</param>
        /// <param name="Query">List of property names, used to find subproperties of the linking resource into which to inject links.</param>
        /// <example>An attributed route returns an array of orders, each containing an array of order lines.  To inject a link into each 
        /// order line detail:
        /// <code>
        /// [Link("orderLineDetail", "api/orders/{OrderNo}/detail/{OrderLineNo}", "Results", "[]", "OrderLines", "[]")]
        /// </code>
        /// </example>
        /// <example>The same example as above, but the resource being returned is a newly created order, so we only want links if the creation of the order was successful: 
        /// <code>
        /// [Link("orderLineDetail", "api/orders/{OrderNo}/detail/{OrderLineNo}", HttpStatusCode.Created, "Results", "[]", "OrderLines", "[]")]
        /// </code>
        /// </example>
        public LinkAttribute(string Rel, string Href, HttpStatusCode Status, params string[] Query) {
            rel = Rel;
            href = Href;
            statusCode = Status;
            query = Query;
            serializer = JsonSerializer.Create(GlobalConfiguration.Configuration.Formatters.JsonFormatter.SerializerSettings);
        }

        #endregion

        #region private methods

        // make a copy of the current LinkAttribute, but drop the first term in the query
        private LinkAttribute NextLayer {
            get {
                LinkAttribute result = new LinkAttribute(this.rel, this.href);
                if (this.query.Length == 1) {
                    result.query = null;
                } else {
                    result.query = new string[this.query.Length - 1];
                    this.query.Skip(1).ToArray().CopyTo(result.query, 0);
                }
                return result;
            }
        }

        /// <summary>
        /// Does a depth-first traversal of the properties of the given object recursively, depth-first.
        /// 
        /// p-code:
        ///   if (link.query is empty) {
        ///     for each member {
        ///       using link.rel and link.href, format a link and attach it to the member;
        ///       ====>>> 
        ///         Q. BUT, __HOW__ do we get the parameters from the request? 
        ///         A. We do not get them from the call, we get them from the response.
        ///       ====>>>
        ///     }
        ///   } else {
        ///       propertyValue = obj.Properties(car(link.query));
        ///       remainingLink = copyOf(link);
        ///       remainingLink.query = cdr(remainingLink.query);
        ///       call walk(propertyValue, remainingLink);
        ///     }
        ///   }
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="link"></param>
        private void Walk(JToken obj, LinkAttribute link) {
            if (link.query == null) {
                JObject linkingObject = obj as JObject;
                // construct an object in serial, then deserialize it and insert it
                var linkInstance = new { rel = link.rel, href = link.href.ResolveParameters(obj) };
                if (linkInstance.href != null) {
                    if (linkingObject.Property("links") == null) {
                        linkingObject.Add("links", new JArray());
                    }
                    (linkingObject.Property("links").Value as JArray).Add(JObject.FromObject(linkInstance));
                }
            } else {
                LinkAttribute remainingLink = link.NextLayer;
                string propertySought = link.query[0];
                JToken propertyValue;
                if (propertySought == "[]") {
                    foreach (JToken arrayMember in (obj as JArray)) {
                        Walk(arrayMember, remainingLink);
                    }
                } else {
                    if ((obj as JObject).TryGetValue(propertySought, out propertyValue) && propertyValue != null) {
                        Walk(propertyValue, remainingLink);
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Extension to string class providing the string.ResolveParameters(this, object) method
    /// </summary>
    public static class StringExtensions {

        /// <summary>
        /// ResolveParameters replaces curly brace parameters in a string with the value found in the object
        /// by using the parameter name to search the object.
        /// 
        /// for example:
        /// 
        /// var widget = new { ID = "12345", ClientName = "Joe", Balance = "9876.54" };
        /// string resolved = "/foo/bar/{ClientName}/baz".ResolveParameters(widget);     // Example (1)
        /// 
        /// results in resolved having a value of "/foo/bar/Joe/baz".
        /// 
        /// You can also do a unique traversal using a dotted notation.
        /// 
        /// "/a/b/{Client.FirstName}/c"                                                      // Example (2)
        ///     .ResolveParameters(
        ///        new { 
        ///           ID = "123456", 
        ///           Client = new { FirstName = "Fred", LastName = "Ngyuen" }, 
        ///           Balance = "98765.43"
        ///      }) 
        /// returns
        /// "/a/b/Fred/c"
        /// 
        /// </summary>
        /// <param name="parameterizedString"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string ResolveParameters(this string parameterizedString, object obj) {
            string result = parameterizedString;
            /* "token" is {ClientName} in Example (1), above */
            foreach (object token in Regex.Matches(parameterizedString, @"{(\.\./)*[a-zA-Z0-9]+}")) {
                /* 
                 * value is "Fred" in Example (2), above:
                 *  1. The token--{Client.FirstName}--is split at the braces, leaving Client.FirstName--the Query Expression
                 *  2. The Query Expression is split at the dots, leaving the Query array ["Client", "FirstName"]
                 *  3. The Query array is passed to GetValue, which recurses into the current object on the current first array entry 
                 */
                object value = GetValue(JObject.FromObject(obj), token.ToString().Split(new char[] { '{', '}' })[1].Split('.'));
                if (value == null)
                    return null;

                result = result.Replace(token.ToString(), value.ToString());
            }
            return result;
        }

        private static object GetValue(JToken obj, params string[] Query) {
            object result = null;
            if (Query == null) {
                result = obj.ToObject<object>();
            } else {
                string propertySought = Query[0];
                JToken propertyValue;
                if ((obj as JObject).TryGetValue(propertySought, out propertyValue)) {
                    string[] remainingQuery;
                    if (Query.Length == 1) {
                        remainingQuery = null;
                    } else {
                        remainingQuery = new string[Query.Length - 1];
                        Query.Skip(1).ToArray().CopyTo(remainingQuery, 0);
                    }
                    result = GetValue(propertyValue, remainingQuery);
                }

            }
            return result;
        }

    }

}

