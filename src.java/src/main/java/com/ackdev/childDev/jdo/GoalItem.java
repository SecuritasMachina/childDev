package com.ackdev.childDev.jdo;

import com.ackdev.common.utility.MyUtility;
import com.fasterxml.jackson.annotation.JsonAutoDetect;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.google.appengine.api.datastore.Text;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;
import com.googlecode.objectify.annotation.OnLoad;
import com.googlecode.objectify.annotation.OnSave;
import com.googlecode.objectify.annotation.Unindex;

import io.swagger.annotations.ApiModel;
import io.swagger.annotations.ApiModelProperty;

@JsonIgnoreProperties(ignoreUnknown = true)

@Entity
@Unindex
@Cache
@JsonAutoDetect
@ApiModel(value = "Goal Setup", description = "Point in time goal entry")
public class GoalItem {
	@Index
	@Id
	@ApiModelProperty(value = "Unique identifier", allowableValues = "guid")
	public String guid;

	@Index
	public String accountFk;

	@Index
	@ApiModelProperty(value = "Expiration Date", allowableValues = "empty,past date,future date")
	public Long expirationDate;

	@Index
	public Long enteredDate = System.currentTimeMillis();

	public Text measureableOutcome;
	public Text goalText;
	public Long nextMeetingDate;
	public Long updatedOn;

	@Index
	public Long completionDate;

	public String tags;

	@OnLoad
	void onLoad() {
	}

	@OnSave
	void onPersist() { /* do something before persisting */
		updatedOn = System.currentTimeMillis();
		if (guid == null)
			guid = MyUtility.makeUUID();

	}

}
