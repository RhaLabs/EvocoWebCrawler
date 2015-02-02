/*
 * Created by SharpDevelop.
 * User: bcrawford
 * Date: 8/13/2014
 * Time: 1:55 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace EvocoWebCrawler
{
  /// <summary>
  /// Description of EvocoWebCrawler.HttpClient
  /// </summary>
  public class HttpClient
  {
    /// <summary>
    ///The client automatically navigates and logs in to https://www.bldgportal.com/Login.aspx
    /// </summary>
    /// <param name="credentials"></param>
    public HttpClient(Credentials.CredentialInterface credentials)
    {
      handler = new HttpClientHandler();
      
      handler.UseCookies = credentials.UseCookies;
      
      handler.AllowAutoRedirect = true;
      
      client = new System.Net.Http.HttpClient(handler);
      
      client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
      
      client.MaxResponseContentBufferSize = System.Int32.MaxValue;
      
      creds = credentials;
      
      this.LogMeIn(@"https://www.bldgportal.com/Login.aspx");
    }
    
    private HttpClientHandler handler;
    
    private System.Net.Http.HttpClient client;
    
    private HttpResponseMessage response;
    
    private Credentials.CredentialInterface creds;
    
    private string viewState = "";
    
    private string eventValidation = "";
    
    private string GlobalEvocoId = "";
    
    private void LogMeIn (string uri)
    {
      var body = this.Navigate(uri);
      
      var navigator = new HtmlNodeNavigator(body);
      
      this.TryGetTokens(navigator, out this.viewState, out this.eventValidation);
      
      var postData = new List< KeyValuePair< string, string > >();
      
      postData.Add( new KeyValuePair< string, string >("__VIEWSTATE", viewState) );
      postData.Add( new KeyValuePair< string, string >("__EVENTVALIDATION", eventValidation) );
      postData.Add( new KeyValuePair< string, string >("UserNameTextBox", creds.Username ) );
      postData.Add( new KeyValuePair< string, string >("PasswordTextBox", creds.Password) );
      postData.Add( new KeyValuePair< string, string >("ConfCheckBox", "on") );
      postData.Add( new KeyValuePair< string, string >("LoginButton", "Authenticate") );
      postData.Add( new KeyValuePair< string, string >("RequestPasswordTextBox", "") );
      
      var content = new FormUrlEncodedContent(postData);
      
      var response = client.PostAsync(uri + "?TIMEOUT=true", content).Result;
      
      navigator = new HtmlNodeNavigator(body);
      
      var doc = navigator.CurrentDocument;
      doc.Save( @"..\login.html" );
      
      uri = @"https://www.bldgportal.com/Application/PortalMain.aspx";
      
      navigator = new HtmlNodeNavigator( this.Navigate(uri) );
      
      doc = navigator.CurrentDocument;
      doc.Save( @"..\portal.html" );
      
      var success = navigator.MoveToId("UserIdTable");
      
      var iterator = navigator.CurrentNode.SelectNodes(@".//a[@onclick]");
      
      var nodeLin = iterator[0];
      
      // get and use the SID to log into the RFI subsystem
      var regex = new System.Text.RegularExpressions.Regex(@"(?<sid>\?SID=.*)'");
      
      var match = regex.Match(nodeLin.Attributes["onclick"].Value);
      
      var group = match.Groups;
      
      this.GlobalEvocoId = group["sid"].Value;
      
    }
    
    private bool TryGetTokens ( HtmlNodeNavigator navigator, out string viewState, out string eventValidation)
    {
      navigator.MoveToRoot();
      
      viewState = "";
      
      eventValidation = "";
      
      var iterator = navigator.CurrentNode.SelectNodes(@".//input[@type='hidden']");
      
      foreach (var node in iterator) {
        var id = node.Id;
        
        var nodeValue = node.GetAttributeValue("value", "");
        
        switch (id) {
          case "__VIEWSTATE":
            viewState = nodeValue;
            
            break;
          case "__EVENTVALIDATION":
            eventValidation = nodeValue;
            
            break;
          default:
            
            break;
        }
      }
      
      return true;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="uri">The path to the resource after the login.  example: http://www.bldgportal.com/RFI/Application/Projects/ProjectList.aspx</param>
    /// <returns>an html document stream</returns>
    public System.IO.Stream Navigate (string uri)
    {
      try {
        var task = client.GetAsync(uri).ContinueWith(
          (result) =>
          { result.Result.EnsureSuccessStatusCode();
            
            return result.Result.Content.ReadAsStreamAsync().Result;
          }
         );
        
        return task.Result;
      } catch (HttpRequestException error) {
        throw error;
      }
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="storeNumber">the store number to lookup</param>
    /// <param name="rfiNumber">the rfi number to lookup</param>
    /// <param name="htmlDocumentStream">the raw html stream of the rfi if we found it</param>
    /// <returns><see cref="bool"/> true if the RFI was found otherwise false</returns>
    public bool TryNavigateStoreRfi (int storeNumber, int sequence, int rfiNumber, out System.IO.Stream htmlDocumentStream)
    {
      // we have to emulate the way the browser works.  Evoco uses javascript onload events for redirection
      // which means that the httpClient doesn't follow to redirects

      #region RfiSubsystemLogin

      //log into the rfi sub system
      var uri = @"http://www.bldgportal.com/RFI/loginDirect.aspx{0}";
      
      this.Navigate( String.Format(uri, this.GlobalEvocoId) );
      #endregion
      
      #region Rfisearch
      // the values from the search box are sent as get parameters
      // here we are doing the same, the page that is returned contains a window.location javascript which
      // is fired at the onload event.  the window.location contians the evoco project ID which we need.
      var uriToStoreRfis = String.Format( "http://www.bldgportal.com/RFI/Application/Projects/ProjectListGrid.aspx?VCP=true&SEARCH_TYPE=StoreNumber&SEARCH_VALUE={0}&SORT_BY=Store&SORT_DIR=ASC", storeNumber.ToString() );
      
      var thisResponse = this.Navigate(uriToStoreRfis);
      
      //extract the evoco id
      var navigator = new HtmlNodeNavigator(thisResponse);
      
      var doc = navigator.CurrentDocument;
      doc.Save( String.Format(@"..\{0}-search.html", storeNumber.ToString()) );
      
      Logger.LoggerAsync.InstanceOf.GeneralLogger.Trace("Attempting to find RFI attachments for {0}", storeNumber.ToString() );
      
      string evocoId = "";
      
      var scriptNode = navigator.SelectSingleNode(".//script[@event='onload']");
      
      var regex = new System.Text.RegularExpressions.Regex(@"PROJECT_ID=(?<id>\d+)");
      
      var match = regex.Match(scriptNode.Value);
      
      // we initally assume that there is only one matching store
      // if that fails then there may be more than one match store
      if (match.Success) {
        var group = match.Groups;

        evocoId = group["id"].Value;
      } else { // if there are more than one matching store, then we are returned a table with the stores.  we need to select the correct one
        // let's first move and test if we can get to the table with id "projectGrid"
        // if we are successful then we can select the correct store.  otherwise something else went wrong
        if ( navigator.MoveToId("projectGrid") ) {
          var headerRow = navigator.CurrentNode.SelectSingleNode(@".//tr[contains(concat(' ',@class,' '), ' DataGridFixedHeader ')]");
          
          var gridRows = navigator.CurrentNode.SelectNodes(@".//tr[not(contains(concat(' ',@class,' '), ' DataGridFixedHeader '))]");
          
          var storeIndexLocation = 0;
          
          // loop over the header rows until we encounter the node that has the word "store".  save the index value so we can quickly jump to that location in the data rows.
          while ( !headerRow.ChildNodes[storeIndexLocation].InnerText.Contains("Store") ) {
            storeIndexLocation ++;
          }
          
          var storeNumberAndSequence = String.Format( "{0}-{1}", storeNumber.ToString(), sequence.ToString("D3") );
          
          foreach (var dataRow in gridRows) {
            if ( dataRow.ChildNodes[storeIndexLocation].InnerText == storeNumberAndSequence ) {
              match = regex.Match( dataRow.GetAttributeValue("onclick", "") );
              
              evocoId = match.Groups["id"].Value;
            }
          }
        }
      }
      
      //use the evoco id to get the list of open RFIs
      var uriToRfiList = String.Format( "http://www.bldgportal.com/RFI/Application/Project/RFI/RFIListGrid.aspx?PROJECT_ID={0}&FILTER_BY=OPEN", evocoId );
      #endregion
      
      #region GetRfiTable
      //find the rfi in the table
      //rfis live in a <table id="RFIGrid"><tr><td></td><td onClick="ShowModalDialog('RFIResponse.aspx?RID=XXXXX','650px','800px');" >XX</td></tr></table>
      //
      
      
      
      //if there are no open RFIs then there is a <span id="noRFISFound"></span> which says no RFI where found.
      
      thisResponse = this.Navigate(uriToRfiList);
      
      navigator = new HtmlNodeNavigator(thisResponse);
      #endregion
      
      #region ProcessRfiRequest
      #region NoOpenRfis
      var success = navigator.MoveToId("noRFISFound");
      
      if (success) {
        // no open Rfis where found
        htmlDocumentStream = new System.IO.MemoryStream();
        
        return false;
        #endregion
        #region FoundOpenRfis
      } else {
        // found some open Rfis
        success = navigator.MoveToId("RFIGrid");
        
        doc = navigator.CurrentDocument;
        doc.Save( String.Format(@"..\{0}.html", storeNumber.ToString()) );
        
        var rowNodes = navigator.CurrentNode.SelectNodes(".//tr[@id]");
        
        #region RfiIteration
        foreach (var row in rowNodes) {
          // the rfi number is found in the third child node
          var cell = row.ChildNodes[2];
          
          #region RfiTestForNumberMatch
          if ( cell.InnerText == rfiNumber.ToString() ) {
            var onClickAttribute = cell.GetAttributeValue("onclick", "");
            
            regex = new System.Text.RegularExpressions.Regex(@"RID=(?<id>\d+)");
            
            match = regex.Match(onClickAttribute);
            
            var group = match.Groups;

            string rfiId = group["id"].Value;
            
            // uri for rfi using rfi id http://www.bldgportal.com/RFI/Application/Project/RFI/RFIResponse.aspx?RID=194553
            var uriToRfi = string.Format("http://www.bldgportal.com/RFI/Application/Project/RFI/RFIResponse.aspx?RID={0}", rfiId);
            
            thisResponse = this.Navigate(uriToRfi);
            
            navigator = new HtmlNodeNavigator(thisResponse);
            
            // the attachments are located in an iframe.  We get the path to post data to from the src attribute of the ifram node
            var iframeNode = navigator.CurrentNode.SelectSingleNode(@"//iframe");
            
            var filePath = iframeNode.GetAttributeValue("src", "");
            
            // here we need to trim off the path notation "../../"
            filePath = filePath.Substring(5);
            
            filePath = filePath.Replace("&amp;", "&");
            
            // concat the file path with the base uri
            var uriToFile = "http://www.bldgportal.com/RFI/Application/" + filePath;
            
            htmlDocumentStream = this.Navigate(uriToFile);
            
            return true;
          }
          #endregion
        }
        #endregion
        // didn't find the RFI in the table
        htmlDocumentStream = new System.IO.MemoryStream();
        
        return false;
      }
      #endregion
      #endregion
    }
    
    public List< KeyValuePair< string, System.IO.Stream > > GetHttpFileAttachment (System.IO.Stream htmlDocumentStream)
    {
      var navigator = new HtmlNodeNavigator(htmlDocumentStream);
      
      // get new view tokens
      this.TryGetTokens(navigator, out this.viewState, out this.eventValidation);
      
      var formAction = navigator.CurrentNode.SelectSingleNode(".//form[@name='attachForm']").GetAttributeValue("action", "");
      
      formAction = formAction.Replace("&amp;", "&");
      
      var uriToFile = "http://www.bldgportal.com/RFI/Application/" + formAction;
      
      // all <a> elements with the "title" attribute
      var fileNodes = navigator.CurrentNode.SelectNodes(@".//a[@title]");
      
      var attachments = new List< KeyValuePair< string, System.IO.Stream > >();
      
      if ( fileNodes == null ) { return attachments; }
      
      foreach (var fileLink in fileNodes) {
        var target = fileLink.Id;
        
        target = target.Replace('_', '$');
        
        var postData = new List< KeyValuePair< string, string > >();
        
        postData.Add( new KeyValuePair< string, string >("__VIEWSTATE", viewState) );
        postData.Add( new KeyValuePair< string, string >("__EVENTVALIDATION", eventValidation) );
        postData.Add( new KeyValuePair< string, string >("__EVENTTARGET", target ) );
        postData.Add( new KeyValuePair< string, string >("__EVENTARGUMENT", "" ) );
        postData.Add( new KeyValuePair< string, string >("validFileTypes", "doc, docx, xls, xlsx, xlsm, dwg, dwgx, dwf, dwfx, bmp, gif, jpg, jpeg, tif, tiff, pdf, txt, rtf, ai") );
        postData.Add( new KeyValuePair< string, string >("fileID", "") );
        
        var content = new FormUrlEncodedContent(postData);
        
        var response = client.PostAsync(uriToFile, content).Result;
        
        //  var fileStream = System.IO.File.Create( String.Format(@"..\{0}",fileLink.GetAttributeValue("title", "") ) );
        // var httpStream = response.Content.ReadAsStreamAsync().Result;
        //        httpStream.CopyTo(fileStream);
//
        // fileStream.Flush();
        attachments.Add( new KeyValuePair< string, System.IO.Stream >
                        ( String.Format(@"..\{0}",fileLink.GetAttributeValue("title", "") ),
                         response.Content.ReadAsStreamAsync().Result) );
      }
      
      return attachments;
    }
    
    async private void DownloadAttachmentAsync ( string url, string path, FormUrlEncodedContent postData )
    {
      var response = await client.PostAsync(url, postData);
      
      using ( var fileStream = System.IO.File.Create(path) ) {
        using ( var httpStream = await response.Content.ReadAsStreamAsync() ) {
          httpStream.CopyTo(fileStream);
          
          fileStream.Flush();
        }
      }
    }
    
    
    private string ToUrlEncodedString (List< KeyValuePair< string, string > > list)
    {
      var builder = new System.Text.StringBuilder();
      
      foreach (var item in list) {
        if (builder.Length > 0) {
          builder.Append("&");
        }
        
        builder.AppendFormat( "{0}={1}", System.Uri.EscapeUriString(item.Key), System.Uri.EscapeUriString(item.Value) );
      }
      
      return builder.ToString();
    }
    
    public void LogMeOut ()
    {
      var postData = new List< KeyValuePair< string, string > >();
      
      postData.Add( new KeyValuePair< string, string >("__VIEWSTATE", viewState) );
      postData.Add( new KeyValuePair< string, string >("__EVENTVALIDATION", eventValidation) );
      postData.Add( new KeyValuePair< string, string >("__EVENTTARGET", "") );
      postData.Add( new KeyValuePair< string, string >("__EVENTARGUMENT", "") );
      postData.Add( new KeyValuePair< string, string >("ctl00$btnLogout", "Update Information Server") );
      
      var content = new FormUrlEncodedContent(postData);
      
      client.PostAsync("https://www.bldgportal.com/Application/PortalMain.aspx", content).ContinueWith(
        (postTask) => { postTask.Result.EnsureSuccessStatusCode(); }
       );
    }
  }
}
