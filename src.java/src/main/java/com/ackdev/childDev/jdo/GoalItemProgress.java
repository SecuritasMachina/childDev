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
public class GoalItemProgress {
	
    @Index
    public String accountFk;
    public Text attendees;
    public Text completedItems;

    @Index
    public Long enteredDate = System.currentTimeMillis();
	
	@Index
    @ApiModelProperty(value = "Expiration Date", allowableValues = "empty,past date,future date")
    public Long expirationDate;
	
	@Index
    public String goalFk;
	
	@Index
    @Id
    @ApiModelProperty(value = "Unique identifier", allowableValues = "guid")
    public String guid;
	public Text location;
	public Text nextLocation;
	public Text nextStepItems;
    public Text notes;
    public String tags;

    @Index
    public Long nextMeetingDate;
    public Long updatedOn;
    
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
