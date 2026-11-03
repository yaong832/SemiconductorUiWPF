using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SemiconductorUi.Models
{
	[XmlRoot("User")]
	public class User
	{
		[XmlElement("Username")]
		public string Username { get; set; }

		[XmlElement("Password")]
		public string Password { get; set; }

		[XmlElement("Role")]
		public string Role { get; set; } // "관리자" 또는 "작업자"

		[XmlElement("CreatedAt")]
		public DateTime CreatedAt { get; set; }

		public User()
		{
			CreatedAt = DateTime.Now;
		}
	}

	[XmlRoot("Users")]
	public class UserList
	{
		[XmlArray("UserList")]
		[XmlArrayItem("User")]
		public List<User> Users { get; set; }

		public UserList()
		{
			Users = new List<User>();
		}
	}
}

