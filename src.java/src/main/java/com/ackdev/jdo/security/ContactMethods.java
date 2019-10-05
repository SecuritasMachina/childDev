package com.ackdev.jdo.security;

import java.util.Date;

import com.googlecode.objectify.Key;
import com.googlecode.objectify.annotation.Cache;
import com.googlecode.objectify.annotation.Entity;
import com.googlecode.objectify.annotation.Id;
import com.googlecode.objectify.annotation.Index;

@Entity
@Index
@Cache
public class ContactMethods {
    private static final long serialVersionUID = 1L;

    /**
     * Old fields below
     */
    @Id
    public Long id;

    public String contactMethod; //phone/email

    public String contactValue; // blah blah

    public Integer priority;

    public Date createdOn = new Date();
    public Date updatedOn;

    public Key<ContactMethods> getKey() {
        return Key.create(ContactMethods.class, id);
    }


}
