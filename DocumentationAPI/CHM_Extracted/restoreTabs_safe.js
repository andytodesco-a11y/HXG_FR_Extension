// set the first tab as active on page load

var tabs = document.getElementsByClassName("tab");

  for (i = 0; i < tabs.length; i++) {
	var column = tabs[i].getElementsByTagName("div");

	if (column.length > 1)
	{
		column[1].click();
	}
	else if (column.length > 0)
	{
		column[0].click();
	}
  }
  
var apiSpecificPageHackElement = document.getElementById("API_SPECIFIC")
if (apiSpecificPageHackElement != null)
{
	apiSpecificPageHackElement.parentElement.style.display = "none";
}

if (!String.prototype.startsWith) {
  String.prototype.startsWith = function(searchString, position) {
    position = position || 0;
    return this.indexOf(searchString, position) === position;
  };
}

//Modify tag [optional]
var params = document.getElementsByClassName("params");
for (i = 0; i < params.length; i++) {
	var rows = params[i].children[0].children;
	for (j = 0; j < rows.length; j++) {
		if (rows[j].children.length == 3 && rows[j].children[2].innerHTML.startsWith('[Optional]') )
		{
			rows[j].children[0].innerHTML = rows[j].children[0].innerHTML.substring(0, rows[j].children[0].innerHTML.length - 1) + ',optional]';
			rows[j].children[2].innerHTML = rows[j].children[2].innerHTML.substr(10);
		}
	}
}