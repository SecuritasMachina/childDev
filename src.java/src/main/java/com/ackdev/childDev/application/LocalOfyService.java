package com.ackdev.childDev.application;

import java.util.logging.Logger;

import com.ackdev.childDev.jdo.GoalItem;
import com.ackdev.childDev.jdo.GoalItemProgress;
import com.ackdev.childDev.jdo.JournalItem;
import com.ackdev.jdo.common.FeedbackItem;
import com.ackdev.jdo.common.SystemHistory;
import com.ackdev.jdo.security.Address;
import com.ackdev.jdo.security.ContactInfo;
import com.ackdev.jdo.security.ContactMethods;
import com.ackdev.jdo.security.ContactsJDO;
import com.ackdev.jdo.security.History;
import com.googlecode.objectify.Objectify;
import com.googlecode.objectify.ObjectifyFactory;
import com.googlecode.objectify.ObjectifyService;

public class LocalOfyService {
    private static final Logger LOG = Logger.getLogger(LocalOfyService.class
            .getName());

    static {
        LOG.info("LocalOfyService 05/20/2019");
        factory().register(FeedbackItem.class);
        factory().register(ContactInfo.class);
        factory().register(ContactsJDO.class);
        factory().register(History.class);
        factory().register(JournalItem.class);
        factory().register(GoalItem.class);
        factory().register(GoalItemProgress.class);
        // Jobs
        factory().register(Address.class);
        factory().register(ContactMethods.class);
        factory().register(SystemHistory.class);
    }

    public static ObjectifyFactory factory() {
        return ObjectifyService.factory();
    }

    public static Objectify ofy() {
        return ObjectifyService.ofy();
    }
}
