  
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