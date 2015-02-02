/*
 * Created by SharpDevelop.
 * User: bcrawford
 * Date: 8/13/2014
 * Time: 2:04 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;

namespace EvocoWebCrawler.Credentials
{
  /// <summary>
  /// Description of Interface1.
  /// </summary>
  public interface CredentialInterface
  {
    string Username { get; set; }

    string Password { get; set; }

    bool UseCookies { get; set; }

  }
}
